using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Geode.Client.Internal;
using Geode.Client.Options;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol;

/// <summary>
/// One framed TCP connection to a Geode server-cache port (default 40404).
/// Mirrors <c>cppcache/src/TcrConnection.cpp</c>.
/// </summary>
internal sealed class TcrConnection(
    IServiceProvider serviceProvider,
    ILogger<TcrConnection> logger,
    CacheScopeContext scopeContext,
    ClientProxyMembershipIdBuilder membershipIdBuilder,
    TcrMessageBuilder messageBuilder)
    : IAsyncDisposable
{

    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    readonly TcpClient _tcpClient = new();
    Stream? _stream;

    // Read options through the scope context so named registrations route
    // to the right cache (plain IOptions<T> always returned the unnamed
    // default). Currently consumed by HandshakeAsync step 7
    // (Subscription.ConflateEvents); Phase 6+ pool / TLS / auth code
    // will read further fields.
    private readonly GeodeClientOptions _options = scopeContext.Options;

    /// <summary>
    /// Server's subscription-queue role, captured from the handshake reply
    /// byte at step 10. Mirrors cppcache <c>hasServerQueue_</c> — despite
    /// the "has" prefix it's an enum, not a bool:
    ///   0 = NON_REDUNDANT_SERVER       (no subscription queue)
    ///   1 = REDUNDANT_PRIMARY_SERVER   (primary HA copy)
    ///   2 = REDUNDANT_SECONDARY_SERVER (secondary HA copy)
    /// MVP doesn't subscribe, so this is informational; Phase 12+ will
    /// branch on it for HA failover.
    /// </summary>
    private byte _hasServerQueue;

    /// <summary>
    /// Number of events currently buffered in the server's subscription
    /// queue for this client, captured from handshake step 11. Mirrors
    /// cppcache <c>queueSize_</c>. Non-zero only after a reconnect with
    /// durable subscriptions — Phase 12+. Default 0.
    /// </summary>
    private int _queueSize;

    /// <summary>
    /// Server's member identity (a serialised <c>InternalDistributedMember</c>),
    /// captured opaque from handshake step 12. <c>null</c> until the
    /// handshake completes. Phase 6 (pool) / 7 (locator) will parse this
    /// to attribute connections to the right server.
    /// </summary>
    private byte[]? _serverMember;

    /// <summary>
    /// Whether the server has delta propagation enabled, captured from
    /// handshake step 14. Mirrors cppcache <c>m_deltaEnabledOnServer</c>.
    /// MVP doesn't send delta updates; recorded for Phase 12+ delta
    /// support so the operation layer can branch on it without redoing
    /// the handshake.
    /// </summary>
    private bool _deltaEnabled;

    private long _createdAt = Stopwatch.GetTimestamp();         // creationTime_ (mutable: UpdateCreationTime resets it)
    private long _lastAccessed = Stopwatch.GetTimestamp();      // lastAccessed_
    // cppcache TcrConnection.cpp:65-70,98 — each conn picks its own [-9, +9]
    // jitter at construction to spread load-conditioning expiry across the
    // pool and avoid synchronised mass-rotation.
    private readonly int _expiryTimeVariancePercentage = RandomNumberGenerator.GetInt32(-9, 10);

    // ── cppcache TcrConnection member mirror (TcrConnection.hpp:272-363) ──
    // Phase 1.5 mirror-then-prune. Most fields are zero / null until
    // the wire path that fills them lands; back-refs are nullable typed
    // so we can swap in real DI plumbing without changing the shape.
#pragma warning disable CS0169, CS0414, CS0649 // placeholder mirror fields wired up phase by phase
    private long _connectionId;                                 // connectionId
    private TcrConnectionManager? _connectionManager;           // connectionManager_
    private TcrEndpoint? _endpointObj;                          // endpointObj_
    // _tcpClient + _stream above cover cppcache `conn_` (Connector).
    private ushort _port;                                       // port_
    private object? _chunksProcessSemaphore;                    // binary_semaphore chunks_process_semaphore_ (≈ SemaphoreSlim)

    private int _isBeingUsed;                                   // volatile bool isBeingUsed_ (Interlocked 0/1)
    private uint _isUsed;                                       // atomic<uint32_t> isUsed_
    private ThinClientPoolDM? _poolDM;                          // poolDM_

#pragma warning restore CS0169, CS0414, CS0649

    /// <summary>
    /// Stamp this connection's last-access time. Mirrors cppcache
    /// <c>TcrConnection::touch()</c>
    /// (<c>cppcache/src/TcrConnection.cpp:1201</c>) — pool managers call
    /// it on borrow / return so <c>cleanStaleConnections</c> /
    /// <see cref="IsIdle"/> can distinguish idle conns from active ones.
    /// </summary>
    public void Touch()
        => Volatile.Write(ref _lastAccessed, Stopwatch.GetTimestamp());

    /// <summary>
    /// Reset both the creation clock and the last-access clock. Mirrors
    /// cppcache <c>TcrConnection::updateCreationTime()</c>
    /// (<c>cppcache/src/TcrConnection.cpp:1222</c>) — the pool calls this
    /// when load-conditioning replacement fails but the conn isn't
    /// expired yet, so the same conn isn't immediately re-elected on
    /// the next <c>cleanStaleConnections</c> sweep.
    /// </summary>
    public void UpdateCreationTime()
    {
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref _createdAt, now);
        Volatile.Write(ref _lastAccessed, now);
    }

    /// <summary>
    /// Has this connection been unused longer than <paramref name="idleTimeout"/>?
    /// Mirrors cppcache <c>TcrConnection::isIdle</c>
    /// (<c>cppcache/src/TcrConnection.cpp:1193</c>).
    /// </summary>
    public bool IsIdle(TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero) return false;
        var elapsed = Stopwatch.GetElapsedTime(Volatile.Read(ref _lastAccessed));
        return elapsed > idleTimeout;
    }

    /// <summary>
    /// Has this connection lived longer than <paramref name="loadConditioningInterval"/>
    /// since it was opened? Mirrors cppcache <c>TcrConnection::hasExpired</c>
    /// (<c>cppcache/src/TcrConnection.cpp:1183</c>).
    /// </summary>
    /// <remarks>
    /// Applies the <see cref="_expiryTimeVariancePercentage"/> jitter from
    /// cppcache (default 0 = exact threshold; non-zero spreads expiry
    /// across a pool to avoid synchronised mass-rotation).
    /// </remarks>
    public bool HasExpired(TimeSpan loadConditioningInterval)
    {
        if (loadConditioningInterval <= TimeSpan.Zero) return false;
        var jitter = loadConditioningInterval * _expiryTimeVariancePercentage / 100;
        var threshold = loadConditioningInterval + jitter;
        return Stopwatch.GetElapsedTime(Volatile.Read(ref _createdAt)) > threshold;
    }

    /// <summary>
    /// Open a TCP connection to <paramref name="host"/>:<paramref name="port"/>
    /// and run the Geode client-to-server handshake. Mirrors
    /// <c>cppcache/src/TcrConnection.cpp::initTcrConnection</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On return the connection is ready to send framed Geode messages.
    /// Bundling TCP connect + handshake in a single entry point matches
    /// cppcache and prevents the easy mistake of forgetting to handshake
    /// (server rejects the first non-handshake frame).
    /// </para>
    /// <para>
    /// MVP only opens request/response channels; notification channels
    /// (subscription / HA secondary) land in Phase 12+ when
    /// <see cref="HandshakeAsync"/>'s <c>isClientNotification</c> /
    /// <c>isSecondary</c> parameters get plumbed through.
    /// </para>
    /// </remarks>
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);

        // Disable Nagle so a 17-byte Ping flushes immediately instead of
        // waiting for buffer fill — cppcache does the same.
        _tcpClient.NoDelay = true;
        await _tcpClient.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("TcrConnection connected to {host}:{port}", host, port);
        _stream = _tcpClient.GetStream();

        // Geode handshake — fail fast here if the server rejects us, so the
        // caller never sees a half-initialised connection.
        await HandshakeAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the Geode client-to-server handshake on the already-connected
    /// stream. Mirrors <c>cppcache/src/TcrConnection.cpp::initTcrConnection</c>
    /// (the portion after <c>createConnection</c>) and
    /// <c>HandShake.cpp</c>.
    /// </summary>
    async Task HandshakeAsync(
        bool isClientNotification = false,
        bool isSecondary = false,
        CancellationToken cancellationToken = default)
    {
        // cppcache precondition: isSecondary only makes sense on a notification
        // channel (it picks PRIMARY vs SECONDARY for HA queue replay).
        if (isSecondary && !isClientNotification)
        {
            throw new ArgumentException(
                $"{nameof(isSecondary)} requires {nameof(isClientNotification)} = true.",
                nameof(isSecondary));
        }

        // Build the whole client-hello in memory; flushed in one SendAsync
        // at the end of the client→server section so the bytes hit the wire
        // as a single TCP segment.
        var helloBuffer = new ArrayBufferWriter<byte>();
        var hello = new BigEndianBinaryWriter(helloBuffer);

        // === Client → Server ====================================================
        //
        // 1. ConnectionType (u8)
        //      100 = CLIENT_TO_SERVER           — request / response (Phase 2–11)
        //      101 = PRIMARY_SERVER_TO_CLIENT   — notification / subscription channel
        //      102 = SECONDARY_SERVER_TO_CLIENT — HA secondary (server keeps the
        //            subscription queue as backup, doesn't actively push)
        const byte ClientToServer = 100;
        const byte PrimaryServerToClient = 101;
        const byte SecondaryServerToClient = 102;
        var connectionType = isClientNotification
            ? (isSecondary ? SecondaryServerToClient : PrimaryServerToClient)
            : ClientToServer;
        hello.WriteByte(connectionType);

        //
        // 2. ProtocolVersion (ordinal only — major/minor/patch never go on the
        //    wire). Compressed form: ordinal ≤ 127 → 1 byte. Uncompressed:
        //    sentinel + i16. See ProtocolVersion.WriteTo.
        ProtocolVersion.Current.WriteTo(hello);
        logger.LogTrace("TcrConnection handshake, sending ProtocolVersion ordinal {Ordinal}",
            ProtocolVersion.Current.Ordinal);
        //
        // 3. ReplyOk (u8) = 59
        //      Tells server we are ready to receive its acceptance reply.
        //      Defined in cppcache/src/TcrConnection.hpp:41 as
        //      `#define REPLY_OK 59`. (The inline comment at TcrConnection.cpp:160
        //      claims 58 — that comment is stale; the macro value 59 is what
        //      actually goes on the wire.)
        const byte ReplyOk = 59;
        hello.WriteByte(ReplyOk);

        //
        // 4. Port set — channel-type dependent, NO bytes for request/response.
        //    cppcache TcrConnection.cpp:161-170:
        //      - !isClientNotification → record local TCP port into a shared set
        //        (Geode uses the set later to identify which client a notification
        //        channel belongs to). NO bytes written here. Skipped entirely until
        //        Phase 6 (pool) / Phase 12+ (subscriptions) need it.
        //      - isClientNotification  → write i32 PortCount + i32 × N port list.
        //        Phase 12+.
        if (isClientNotification)
        {
            throw new NotImplementedException(
                "Notification-channel handshake (port-set list) is not " +
                "implemented; subscription support lands in Phase 12+.");
        }

        //
        // 5. ReadTimeout (i32) — request/response channel only.
        //    int.MaxValue - 10000 (~24.85 days, "effectively no timeout"). The
        //    -10000 dodges an old GFE 5.7 bug where the server added a 5-sec
        //    buffer that would otherwise overflow int.MaxValue.
        //    Notification channels skip this field (server is the sender, no
        //    timeout to set).
        if (!isClientNotification)
        {
            const int HandshakeReadTimeoutMillis = int.MaxValue - 10000;
            hello.WriteInt32(HandshakeReadTimeoutMillis);
        }

        //
        // 6. ClientProxyMembershipID — one DataSerializable object on the wire.
        //    Java client writes this as `DataSerializer.writeObject(id, out)`;
        //    server reads it as `ClientProxyMembershipID.readCanonicalized(in)`
        //    which internally calls `DataSerializer.readObject`. The single
        //    `writeObject` call expands into FOUR sequential wire pieces:
        //
        //       6a. FixedIDByte    (u8 = 1)   ← DataSerializableFixedID byte form
        //       6b. DSFid          (u8 = 38)  ← ClientProxyMembershipId class id
        //       6c. identity       (varint length + bytes)  ← cppcache m_memID;
        //                                                     opaque blob containing
        //                                                     a serialised
        //                                                     InternalDistributedMember
        //                                                     (hostname, PID, version,…)
        //       6d. uniqueId       (i32)      ← reconnect / sync counter; 1 for fresh client
        //
        //    Constants: cppcache/include/geode/internal/DSCode.hpp:28
        //    (FixedIDByte = 1) and DSFixedId.hpp:47 (ClientProxyMembershipId = 38).
        //    TODO: extract DSCode / DSFid enums once Phase 3+ accumulates values.
        //    The 6c identity bytes are produced by ClientProxyMembershipIdBuilder
        //    (mirrors cppcache ClientProxyMembershipIDFactory + initObjectVars).
        const byte ClientProxyMembershipIdDsfid = 38;
        const int FreshClientUniqueId = 1;
        hello.WriteByte(DSCode.FixedIDByte);                   // 6a
        hello.WriteByte(ClientProxyMembershipIdDsfid);         // 6b
        hello.WriteBytes(membershipIdBuilder.Build());         // 6c (varint length + bytes)
        hello.WriteInt32(FreshClientUniqueId);                 // 6d

        //
        // 7. Overrides (byte[] on the Java side, but always length 1 so far).
        //    Java: `for (byte b : getOverrides()) hdos.writeByte(b)`.
        //    Server reads ONE byte: `setOverrides(new byte[] { readByte() })`.
        //    Currently only conflation override is encoded here, sourced
        //    from GeodeClientOptions.Subscription.ConflateEvents:
        //       null  → 0 (use server default)
        //       true  → 1 (force conflation on)
        //       false → 2 (force conflation off)
        //    TODO: keep an eye on Java geode-core widening this array.
        hello.WriteByte(MapConflateEvents());

        //
        // 8. Security mode + optional credentials body.
        //       SECURITY_CREDENTIALS_NONE              = 0   ← MVP
        //       SECURITY_CREDENTIALS_NORMAL            = 1   ← Phase 9 auth
        //       SECURITY_MULTIUSER_NOTIFICATIONCHANNEL = 3
        //    (TcrConnection.hpp:49-51 / Java Handshake.java)
        //    When mode != NONE, Properties body follows immediately. NONE skips it.
        const byte SecurityCredentialsNone = 0;
        hello.WriteByte(SecurityCredentialsNone);

        // Flush the whole client-hello in one SendAsync. NoDelay is on
        // (set in ConnectAsync), so this lands as a single TCP segment;
        // the server reads it as one contiguous handshake.
        var clientHello = helloBuffer.WrittenSpan.ToArray();
        logger.LogTrace("TcrConnection sending client-hello ({byteCount} bytes)", clientHello.Length);
        await SendAsync(clientHello, cancellationToken).ConfigureAwait(false);

        //
        // === Server → Client ====================================================
        //  Order taken from ClientSideHandshakeImpl.handshakeWithServer (Java).
        //
        //  9. AcceptanceCode (u8)
        //       59 = OK (Handshake.java:58 REPLY_OK).
        //       60 REFUSED / 61 INVALID / 66 AUTH_NOT_REQUIRED — server keeps
        //                                                       sending steps 10-14.
        //       67 SERVER_IS_LOCATOR / 21 SSL_REQUIRED — server stops here, no
        //                                                more bytes to read.
        //     Strategy: throw immediately for the "no more data" codes (matches
        //     Java client). For other non-OK codes, capture the byte and keep
        //     reading so step 13's diagnostic text can land in the exception.
        const byte ReplyOkServer = 59;
        const byte ReplyServerIsLocator = 67;
        const byte ReplySslRequired = 21;
        var acceptanceCode = (await ReadHandshakeDataAsync(1, cancellationToken)
            .ConfigureAwait(false))[0];
        if (acceptanceCode == ReplySslRequired)
        {
            throw new GeodeException(
                "Geode server requires SSL but client connected in plaintext.");
        }
        if (acceptanceCode == ReplyServerIsLocator)
        {
            throw new GeodeException(
                "Connected port belongs to a Geode locator, not a server. " +
                "Use locator-discovery configuration instead of pointing at this address directly.");
        }
        // Any other non-OK code → defer the throw until after step 13 so we
        // can surface the server's diagnostic message in the exception.
        //
        // 10. EndpointType / ServerQueueStatus (u8). Identifies the server's
        //     subscription role (NON_REDUNDANT_SERVER / PRIMARY / SECONDARY).
        //     MVP doesn't subscribe, but we record the value into
        //     _hasServerQueue so Phase 12+ HA failover can branch on it
        //     without re-running the handshake.
        _hasServerQueue = (await ReadHandshakeDataAsync(1, cancellationToken)
            .ConfigureAwait(false))[0];
        logger.LogTrace("TcrConnection handshake hasServerQueue = {hasServerQueue}", _hasServerQueue);

        //
        // 11. QueueSize (i32). Number of events currently buffered in the
        //     server's subscription queue for this client. Non-zero only
        //     after reconnect with durable subscriptions; recorded into
        //     _queueSize for Phase 12+ to consume.
        var queueSizeBuf = await ReadHandshakeDataAsync(4, cancellationToken)
            .ConfigureAwait(false);
        _queueSize = BinaryPrimitives.ReadInt32BigEndian(queueSizeBuf);
        logger.LogTrace("TcrConnection handshake queueSize = {queueSize}", _queueSize);
        //
        // 12. ServerMember — varint length + N opaque bytes (the server's
        //     serialised InternalDistributedMember). Read via
        //     DataSerializer.readByteArray on the Java side; same encoding
        //     as our WriteArrayLen / WriteBytes pair. We capture the bytes
        //     into _serverMember without parsing — Phase 6/7 will decode.
        var serverMemberLen = await ReadHandshakeArrayLenAsync(cancellationToken)
            .ConfigureAwait(false);
        _serverMember = serverMemberLen > 0
            ? await ReadHandshakeDataAsync(serverMemberLen, cancellationToken).ConfigureAwait(false)
            : [];
        logger.LogTrace("TcrConnection handshake serverMember = {byteCount} bytes", _serverMember.Length);
        //
        // 13. Message — Java writeUTF format (u16 byte-length + modified UTF-8).
        //     Server's diagnostic / refusal text; empty on the success path,
        //     populated on REFUSED / INVALID / AUTH_NOT_REQUIRED / etc.
        //     Captured into serverMessage and folded into the GeodeException
        //     thrown after step 14 when AcceptanceCode != REPLY_OK.
        //     Modified-UTF-8 vs standard UTF-8 only differs at U+0000 and
        //     supplementary code points; English diagnostic text decodes
        //     identically with Encoding.UTF8.
        var messageLenBuf = await ReadHandshakeDataAsync(2, cancellationToken)
            .ConfigureAwait(false);
        var messageLen = BinaryPrimitives.ReadUInt16BigEndian(messageLenBuf);
        var messageBytes = messageLen > 0
            ? await ReadHandshakeDataAsync(messageLen, cancellationToken).ConfigureAwait(false)
            : [];
        var serverMessage = Encoding.UTF8.GetString(messageBytes);
        logger.LogTrace("TcrConnection handshake serverMessage = '{serverMessage}'", serverMessage);
        //
        // 14. DeltaEnabled (u8 read as bool: 0 = false, non-zero = true).
        //     Server's delta-propagation toggle; recorded into _deltaEnabled
        //     for Phase 12+ to branch on. Not actioned in MVP.
        _deltaEnabled = (await ReadHandshakeDataAsync(1, cancellationToken)
            .ConfigureAwait(false))[0] != 0;
        logger.LogTrace("TcrConnection handshake deltaEnabled = {deltaEnabled}", _deltaEnabled);

        // Deferred from step 9: now that the full server response is drained
        // (so the stream is in a clean state for the caller's next move) and
        // the diagnostic text from step 13 is in hand, surface any non-OK
        // acceptance code as a GeodeException with the message attached.
        if (acceptanceCode != ReplyOkServer)
        {
            var detail = string.IsNullOrEmpty(serverMessage)
                ? "(no message)"
                : $"\"{serverMessage}\"";
            throw new GeodeException(
                $"Geode server refused handshake; AcceptanceCode = {acceptanceCode}. Server says: {detail}.");
        }
        //
        // ========================================================================
        // Implementation strategy for the server response: read each field
        // off _stream with ReadHandshakeDataAsync + BinaryPrimitives, and
        // validate / drain as listed above.
    }

    /// <summary>
    /// Map the tristate <see cref="SubscriptionOptions.ConflateEvents"/>
    /// to the wire byte used in the handshake "overrides" field. Mirrors
    /// cppcache <c>TcrConnection::getOverrides</c>.
    /// </summary>
    private byte MapConflateEvents() => _options.Subscription.ConflateEvents switch
    {
        null => 0,   // CONFLATION_DEFAULT — let the server decide
        true => 1,   // CONFLATION_ON
        false => 2,   // CONFLATION_OFF
    };

    /// <summary>
    /// Read exactly <paramref name="byteCount"/> bytes from the underlying
    /// stream — the handshake's ad-hoc, non-framed read primitive. Mirrors
    /// cppcache <c>TcrConnection::readHandshakeData</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Stream.ReadExactlyAsync(Memory{byte}, CancellationToken)"/>
    /// already handles partial reads + cancellation, so this helper is just
    /// "allocate buffer + read into it" as a single named step that reads
    /// well at the call site.
    /// </remarks>
    private async Task<byte[]> ReadHandshakeDataAsync(
        int byteCount,
        CancellationToken cancellationToken)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before reading handshake data.");

        var buf = new byte[byteCount];
        await stream.ReadExactlyAsync(buf, cancellationToken).ConfigureAwait(false);
        return buf;
    }

    /// <summary>
    /// Read Geode's variable-length array-length encoding from the stream.
    /// Inverse of <see cref="BigEndianBinaryWriter.WriteArrayLen"/> /
    /// cppcache <c>DataInput::readArrayLen</c>:
    /// <list type="bullet">
    ///   <item>first byte == <c>-1</c> (0xFF) → <c>-1</c> (null sentinel).</item>
    ///   <item>first byte == <c>-2</c> (0xFE) → u16 length follows (3 bytes total).</item>
    ///   <item>first byte == <c>-3</c> (0xFD) → i32 length follows (5 bytes total).</item>
    ///   <item>otherwise (0–252) → first byte itself is the length.</item>
    /// </list>
    /// </summary>
    private async Task<int> ReadHandshakeArrayLenAsync(CancellationToken cancellationToken)
    {
        var first = (sbyte)(await ReadHandshakeDataAsync(1, cancellationToken)
            .ConfigureAwait(false))[0];
        return first switch
        {
            -1 => -1,
            -2 => BinaryPrimitives.ReadUInt16BigEndian(
                await ReadHandshakeDataAsync(2, cancellationToken).ConfigureAwait(false)),
            -3 => BinaryPrimitives.ReadInt32BigEndian(
                await ReadHandshakeDataAsync(4, cancellationToken).ConfigureAwait(false)),
            _ => first,
        };
    }

    /// <summary>
    /// Write a fully-encoded frame to the wire and flush.
    /// </summary>
    /// <remarks>
    /// Pure transport: the caller (operation layer) is responsible for
    /// producing <paramref name="data"/> via <see cref="TcrMessage.Encode"/>
    /// or equivalent. Mirrors <c>cppcache/src/TcrConnection.cpp::send</c>.
    /// Assumes the connection is already open; <see cref="TcpClient.GetStream"/>
    /// throws <see cref="InvalidOperationException"/> otherwise.
    /// </remarks>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before {nameof(SendAsync)}.");

        logger.LogTrace("TcrConnection sending {ByteCount} bytes", data.Length);

        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Read one framed message from the wire: 17-byte header followed by
    /// the <c>MessageLength</c> body bytes the header advertises.
    /// </summary>
    /// <returns>
    /// The full frame (header + body) as a contiguous byte array, ready to
    /// be handed to <see cref="TcrMessage.Decode"/> by the caller.
    /// </returns>
    /// <exception cref="EndOfStreamException">
    /// The peer closed the connection before a full frame was received.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The header advertises a negative <c>MessageLength</c>.
    /// </exception>
    /// <remarks>
    /// Pure transport: decoding the bytes back into a <see cref="TcrMessage"/>
    /// is the caller's job. Mirrors <c>cppcache/src/TcrConnection.cpp::readMessage</c>.
    /// </remarks>
    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must be called before {nameof(ReceiveAsync)}.");

        // 1. Read the fixed-length header so we know how many body bytes
        //    to expect. Header offsets:
        //       0  i32 MessageType
        //       4  i32 MessageLength    <- bytes occupied by the Parts payload
        //       8  i32 NumParts
        //      12  i32 TransactionId
        //      16  u8  EarlyAck
        var frame = new byte[TcrMessage.HeaderLength];
        await stream
            .ReadExactlyAsync(frame.AsMemory(0, TcrMessage.HeaderLength), cancellationToken)
            .ConfigureAwait(false);

        var messageLength = BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4, sizeof(int)));
        if (messageLength < 0)
        {
            throw new InvalidDataException(
                $"Received header advertises negative MessageLength={messageLength}.");
        }

        logger.LogTrace(
            "TcrConnection received header, MessageLength={messageLength}", messageLength);

        // 2. Grow the buffer and read the parts payload, if any.
        if (messageLength > 0)
        {
            Array.Resize(ref frame, TcrMessage.HeaderLength + messageLength);
            await stream
                .ReadExactlyAsync(
                    frame.AsMemory(TcrMessage.HeaderLength, messageLength), cancellationToken)
                .ConfigureAwait(false);
        }

        return frame;
    }

    /// <summary>
    /// Send a <see cref="TcrMessage"/> request and read the next framed
    /// message from the wire as the reply. The message-level building
    /// block on top of <see cref="SendAsync"/> / <see cref="ReceiveAsync"/>;
    /// every operation (Ping, Put, Get, …) ultimately composes through
    /// here. Mirrors cppcache <c>TcrConnection::sendRequest</c>.
    /// </summary>
    /// <remarks>
    /// Pure request-response: assumes one in-flight request per
    /// connection. Doesn't interpret the reply — callers branch on
    /// <see cref="TcrMessage.MessageType"/> themselves (e.g. Reply vs
    /// Exception). Phase 6 connection-pool dispatch will lift this to be
    /// the only public entry point used by the operation layer.
    /// </remarks>
    public async Task<TcrMessage> SendRequestAsync(
        TcrMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await SendAsync(request.Encode(), cancellationToken).ConfigureAwait(false);
        var replyBytes = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        return TcrMessage.Decode(replyBytes);
    }

    /// <summary>
    /// Chunked-reply variant of <see cref="SendRequestAsync(TcrMessage, CancellationToken)"/>.
    /// Sends the request, reads the first-frame header, and loops the
    /// chunk-header / chunk-body pair until the flags byte's
    /// <c>LAST_CHUNK</c> bit is set, handing each chunk body to
    /// <paramref name="chunkedResult"/>. Mirrors cppcache
    /// <c>TcrConnection::sendRequestForChunkedResponse</c> &#x2192;
    /// <c>readMessageChunked</c>
    /// (<c>cppcache/src/TcrConnection.cpp:755-799</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Wire-format differs from the single-message path.</b> The first
    /// 17-byte header for a chunked reply is laid out as
    /// <c>[msgType i32][numberOfParts i32][txId i32][chunkLength i32][flags u8]</c>
    /// (cppcache <c>TcrConnection::readResponseHeader</c>,
    /// <c>:810-851</c>), <b>not</b> the
    /// <c>[msgType][msgLength][numParts][txId][earlyAck]</c> shape that
    /// <see cref="ReceiveAsync"/> parses. Subsequent chunk headers are
    /// 5 bytes &#x2014; <c>[chunkLength i32][flags u8]</c>
    /// (<c>readChunkHeader</c>, <c>:853-887</c>). The server picks the
    /// layout based on the request opcode; the client must read the
    /// shape it asked for.
    /// </para>
    /// <para>
    /// <b>Flags byte.</b> Bit 0 (<see cref="LastChunkMask"/>) marks the
    /// final chunk &#x2014; loop exit. Bit 1 indicates a trailing secure
    /// part (auth); cppcache reads it via
    /// <c>readSecureObjectPart</c> inside the result handler. Phase 1.3
    /// no auth = bit 1 always 0.
    /// </para>
    /// <para>
    /// <b>Returned <see cref="TcrMessage"/>.</b> Carries the header
    /// fields (<see cref="TcrMessage.MessageType"/> and
    /// <see cref="TcrMessage.TransactionId"/>) so callers can branch on
    /// <c>RESPONSE</c> / <c>REPLY</c> / <c>EXCEPTION</c>; the
    /// <see cref="TcrMessage.Parts"/> list is empty &#x2014; chunked
    /// payload lives in <paramref name="chunkedResult"/>. The
    /// <c>numberOfParts</c> header field is discarded (Phase 1.3 doesn't
    /// surface it; if a caller ever needs it, the record can grow a
    /// <c>NumberOfParts</c> slot).
    /// </para>
    /// <para>
    /// <b>Exception replies.</b> When the first-frame
    /// <c>messageType</c> is <see cref="MessageType.Exception"/> the
    /// loop still runs &#x2014; cppcache packs the exception payload
    /// into chunks just like a normal response. The handler should
    /// accumulate / inspect them as needed; this method returns
    /// normally with the Exception message type, and the caller throws.
    /// (Phase 1.3 callers use the message type alone for the throw
    /// path; surfacing the actual exception text from chunk bytes lands
    /// when integration tests demand it.)
    /// </para>
    /// </remarks>
    public async Task<TcrMessage> SendRequestAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(chunkedResult);

        // Send (same path as the single-message overload).
        await SendAsync(request.Encode(), cancellationToken).ConfigureAwait(false);

        // First-frame header (different layout from non-chunked path).
        var (msgType, numberOfParts, txId, chunkLen, flags) =
            await ReadChunkedResponseHeaderAsync(cancellationToken).ConfigureAwait(false);

        logger.LogTrace(
            "TcrConnection chunked reply header: type={MsgType}, parts={NumParts}, " +
            "txId={TxId}, firstChunkLen={ChunkLen}, flags=0x{Flags:X2}",
            msgType, numberOfParts, txId, chunkLen, flags);

        chunkedResult.Reset();

        // Chunk loop. Read body of advertised length, hand to result,
        // peek lastChunk flag — if not set, pull next 5-byte chunk
        // header and repeat. Mirrors cppcache while-processChunk.
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must complete before chunked send.");

        while (true)
        {
            if (chunkLen < 0)
            {
                throw new InvalidDataException(
                    $"Chunk header advertises negative chunkLength={chunkLen}.");
            }

            var body = new byte[chunkLen];
            if (chunkLen > 0)
            {
                await stream
                    .ReadExactlyAsync(body.AsMemory(0, chunkLen), cancellationToken)
                    .ConfigureAwait(false);
            }

            var isLastChunk = (flags & LastChunkMask) != 0;
            chunkedResult.HandleChunk(body, isLastChunk);

            if (isLastChunk)
            {
                break;
            }

            (chunkLen, flags) = await ReadChunkHeaderAsync(cancellationToken).ConfigureAwait(false);
        }

        // Synthesise a TcrMessage carrying just the header fields the
        // caller branches on. Body is owned by chunkedResult.
        return new TcrMessage(
            MessageType: (MessageType)msgType,
            TransactionId: txId,
            EarlyAck: 0,
            Parts: Array.Empty<TcrPart>());
    }

    /// <summary>
    /// <c>lastChunkAndSecurityFlags</c> bit 0 &#x2014; this is the final
    /// chunk in the reply. Mirrors cppcache <c>LAST_CHUNK_MASK</c>
    /// (<c>cppcache/src/TcrMessage.cpp</c>).
    /// </summary>
    private const byte LastChunkMask = 0x01;

    /// <summary>
    /// Read the 17-byte first-frame header for a chunked reply
    /// (<c>msgType i32, numberOfParts i32, txId i32, chunkLength i32,
    /// flags u8</c>). Mirrors cppcache
    /// <c>TcrConnection::readResponseHeader</c>
    /// (<c>cppcache/src/TcrConnection.cpp:810-851</c>).
    /// </summary>
    private async Task<(int MsgType, int NumberOfParts, int TxId, int ChunkLen, byte Flags)>
        ReadChunkedResponseHeaderAsync(CancellationToken ct)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must complete before chunked send.");

        var buffer = new byte[TcrMessage.HeaderLength];
        await stream
            .ReadExactlyAsync(buffer.AsMemory(0, TcrMessage.HeaderLength), ct)
            .ConfigureAwait(false);

        return (
            MsgType: BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)),
            NumberOfParts: BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4)),
            TxId: BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8, 4)),
            ChunkLen: BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12, 4)),
            Flags: buffer[16]);
    }

    /// <summary>
    /// Read a 5-byte continuation chunk header
    /// (<c>chunkLength i32, flags u8</c>). Mirrors cppcache
    /// <c>TcrConnection::readChunkHeader</c>
    /// (<c>cppcache/src/TcrConnection.cpp:853-887</c>).
    /// </summary>
    private async Task<(int ChunkLen, byte Flags)> ReadChunkHeaderAsync(CancellationToken ct)
    {
        var stream = _stream
            ?? throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must complete before chunked send.");

        const int ChunkHeaderLength = 5;
        var buffer = new byte[ChunkHeaderLength];
        await stream
            .ReadExactlyAsync(buffer.AsMemory(0, ChunkHeaderLength), ct)
            .ConfigureAwait(false);

        return (
            ChunkLen: BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4)),
            Flags: buffer[4]);
    }

    /// <summary>
    /// Send a <see cref="MessageType.Ping"/> (5) and wait for the server's
    /// <see cref="MessageType.Reply"/> (6). Mirrors cppcache
    /// <c>TcrMessagePing</c>.
    /// </summary>
    /// <exception cref="GeodeException">
    /// Server returned a <see cref="TcrMessage.MessageType"/> other than
    /// <see cref="MessageType.Reply"/> (e.g. an Exception reply carrying
    /// error text in its parts).
    /// </exception>
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        var reply = await SendRequestAsync(messageBuilder.Ping(), cancellationToken).ConfigureAwait(false);
        if (reply.MessageType != MessageType.Reply)
        {
            throw new GeodeException(
                $"Expected Reply ({(int)MessageType.Reply}) to Ping, got " +
                $"{reply.MessageType} ({(int)reply.MessageType}).");
        }
    }

    /// <summary>
    /// Polite shutdown: send <see cref="MessageType.CloseConnection"/>
    /// (18) so the server frees this socket's session immediately, then
    /// <see cref="DisposeAsync"/> the underlying transport. Mirrors
    /// cppcache <c>TcrConnection::close()</c>
    /// (<c>cppcache/src/TcrConnection.cpp:933-951</c>).
    /// </summary>
    /// <param name="keepAlive">
    /// Tells the server whether to keep this client's subscription queue
    /// (Phase 2+ HA / durable client). Phase 1.1 callers always pass
    /// <c>false</c> — we have no subscription state worth preserving.
    /// </param>
    /// <remarks>
    /// Fire-and-forget: cppcache does not await any reply (the server
    /// just closes its side after receiving the frame) and swallows
    /// every exception (<c>LOGINFO</c> only) — by definition this is
    /// the destruction path, so a half-dead socket failing the write is
    /// not an error worth propagating.
    /// </remarks>
    public async Task CloseAsync(bool keepAlive, CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }

        // Builder is ctor-injected; cppcache pulls it lazily off DataOutput.
        var closeMsg = messageBuilder.CloseConnection(keepAlive);

        // 2-second send budget mirrors cppcache TcrConnection.cpp:944
        // (`send(..., std::chrono::seconds(2), false)`). The connection is
        // dying anyway — don't let a slow / half-dead socket hold up shutdown.
        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sendCts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await SendAsync(closeMsg.Encode(), sendCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // cppcache LOGINFO("Close connection message failed with msg: %s")
            // (TcrConnection.cpp:947). By definition we're tearing down — a
            // failed write isn't actionable, just informational. Caller's ct
            // cancellation flows through but we still dispose below.
            logger.LogInformation(ex, "Close connection message failed");
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Dispose the stream first so any pending async work (e.g. TLS
        // close_notify once Phase 8 swaps in SslStream) gets a chance to
        // flush; then drop the underlying socket. _stream is null if
        // ConnectAsync was never called.
        if (_stream is not null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        _tcpClient.Dispose();
    }
}
