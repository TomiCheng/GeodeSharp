using System.Buffers;
using System.Runtime.InteropServices;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-scoped helper that talks to locators on behalf of a
/// <see cref="ThinClientPoolDM"/>. Mirrors cppcache
/// <c>ThinClientLocatorHelper</c>
/// (<c>cppcache/src/ThinClientLocatorHelper.hpp/.cpp</c>): owns the
/// pool's current view of the locator list, sends locator-protocol
/// requests, and refreshes the list periodically.
/// </summary>
/// <remarks>
/// cppcache full surface (4 public methods): <c>updateLocators</c>,
/// <c>getEndpointForNewFwdConn</c>, <c>getEndpointForNewCallBackConn</c>,
/// <c>getAllServers</c>. We currently implement the first two; the
/// subscription-channel and metadata variants are Phase 2+ / Phase 4+.
/// SNI proxy fields (cppcache <c>m_sniProxyHost</c> /
/// <c>m_sniProxyPort</c>) are deferred to Phase 3 TLS work.
/// </remarks>
internal sealed class ThinClientLocatorHelper(
    List<ServerLocation> initialLocators,
    int connectionRetries,
    IServiceProvider serviceProvider,
    ILogger<ThinClientLocatorHelper> logger)
{
    /// <summary>cppcache <c>ThinClientLocatorHelper.cpp:117</c> — magic int prefix to every locator request.</summary>
    private const int GossipVersion = 1002;

    /// <summary>cppcache <c>TcrConnection.hpp:44</c>: first byte the locator sends when it requires SSL but the client did not enable TLS.</summary>
    private const byte ReplySslEnabled = 21;

    private readonly List<ServerLocation> _locators = [.. initialLocators];
    private readonly Lock _swapLock = new();
    // Caller (ThinClientPoolDM) guarantees >= 0 via CachePoolOptions
    // validator + default 3. cppcache's getConnRetries() sentinel-resolves
    // <=0 to 3; we surface the resolved default at the Options layer so 0
    // can mean "no retries" end-to-end.
    private readonly int _connectionRetries = connectionRetries;

    // ─────────────────────────────────────────────────────────────
    //  Public surface
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Refresh the locator list from the cluster. Mirrors cppcache
    /// <c>ThinClientLocatorHelper::updateLocators</c>
    /// (<c>ThinClientLocatorHelper.cpp:281-313</c>): walk a shuffled
    /// snapshot of locators, ask the first one that responds for the
    /// authoritative set, merge with the client's known set (preserving
    /// client-known entries the server didn't echo back), and swap
    /// atomically. Throws <see cref="GeodeException"/> only when no
    /// locator could be reached at all.
    /// </summary>
    public async Task UpdateLocatorsAsync(string serverGroup, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serverGroup);

        var snapshot = SnapshotShuffledLocators();
        var requestBytes = BuildLocatorListRequestFrame(serverGroup);

        foreach (var loc in snapshot)
        {
            logger.LogTrace(
                "ThinClientLocatorHelper: querying locator [{Host}:{Port}] for serverGroup='{Group}'",
                loc.Host, loc.Port, serverGroup);

            var response = await TrySendAsync(
                loc, requestBytes, DSFid.LocatorListResponse,
                LocatorListResponse.ReadFrom, ct).ConfigureAwait(false);
            if (response is null) continue;

            var merged = Merge(response.Locators, snapshot);
            SwapLocators(merged);
            logger.LogDebug(
                "ThinClientLocatorHelper: refreshed locator list via [{Host}:{Port}]; old size {Old}, new size {New}, isBalanced={Balanced}",
                loc.Host, loc.Port, snapshot.Count, merged.Count, response.IsBalanced);
            return;
        }

        throw new GeodeException(
            $"updateLocators(serverGroup='{serverGroup}'): no locator reachable " +
            $"from {snapshot.Count} configured.");
    }

    /// <summary>
    /// Ask the locator pool for one server to open a forward
    /// (client → server) connection on. Mirrors cppcache
    /// <c>ThinClientLocatorHelper::getEndpointForNewFwdConn</c>
    /// (<c>ThinClientLocatorHelper.cpp:222-279</c>).
    /// </summary>
    /// <remarks>
    /// Two failure modes the caller cares about:
    /// <list type="bullet">
    ///   <item>
    ///     All locators unreachable → cppcache
    ///     <c>NoAvailableLocatorsException</c>; we surface as
    ///     <see cref="GeodeException"/>.
    ///   </item>
    ///   <item>
    ///     Some locator answered but no server matched the group →
    ///     cppcache <c>NotConnectedException("No servers found")</c>; we
    ///     surface as <see cref="GeodeException"/> with the message.
    ///   </item>
    /// </list>
    /// <c>ClientReplacementRequest</c> (cppcache failover path when a
    /// specific <c>currentServer</c> is known to be unhealthy) is
    /// deferred; this entry point only sends <c>ClientConnectionRequest</c>.
    /// </remarks>
    public async Task<ServerLocation> GetEndpointForNewFwdConnAsync(
        string serverGroup,
        IReadOnlyCollection<ServerLocation> excludeServers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serverGroup);
        ArgumentNullException.ThrowIfNull(excludeServers);

        var snapshot = SnapshotShuffledLocators();
        if (snapshot.Count == 0)
        {
            throw new GeodeException("getEndpointForNewFwdConn: no locators configured / known.");
        }

        var requestBytes = BuildClientConnectionRequestFrame(serverGroup, excludeServers);

        var locatorFound = false;
        // cppcache iterates `attempt < maxAttempts` cycling locators mod size,
        // so the same locator can be retried until the attempt budget runs out.
        for (var attempt = 0; attempt < _connectionRetries; attempt++)
        {
            var loc = snapshot[attempt % snapshot.Count];
            logger.LogTrace(
                "ThinClientLocatorHelper: asking locator [{Host}:{Port}] for server in group='{Group}'",
                loc.Host, loc.Port, serverGroup);

            var response = await TrySendAsync(
                loc, requestBytes, DSFid.ClientConnectionResponse,
                ClientConnectionResponse.ReadFrom, ct).ConfigureAwait(false);
            if (response is null) continue;

            if (!response.ServerFound)
            {
                // Locator was reachable but reported no eligible server —
                // remember that so we can distinguish "no locator reachable"
                // from "locators say cluster is empty" at the end.
                locatorFound = true;
                logger.LogTrace(
                    "ThinClientLocatorHelper: locator [{Host}:{Port}] reports no server in group='{Group}'",
                    loc.Host, loc.Port, serverGroup);
                continue;
            }

            // Server found — response.Server is non-null when ServerFound=true
            // (enforced by ClientConnectionResponse.ReadFrom).
            var server = response.Server!;
            logger.LogDebug(
                "ThinClientLocatorHelper: locator [{Host}:{Port}] returned server [{ServerHost}:{ServerPort}] for group='{Group}'",
                loc.Host, loc.Port, server.Host, server.Port, serverGroup);
            return server;
        }

        // Out of attempts — cppcache distinguishes the two failure modes.
        if (locatorFound)
        {
            throw new GeodeException(
                $"getEndpointForNewFwdConn(serverGroup='{serverGroup}'): " +
                $"no server found across {_connectionRetries} attempts.");
        }
        throw new GeodeException(
            $"getEndpointForNewFwdConn(serverGroup='{serverGroup}'): " +
            $"no locator reachable across {_connectionRetries} attempts.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Snapshot + atomic swap
    // ─────────────────────────────────────────────────────────────

    /// <summary>Lock + copy + shuffle. Mirrors cppcache <c>getLocators()</c> (<c>ThinClientLocatorHelper.cpp:75-85</c>).</summary>
    private List<ServerLocation> SnapshotShuffledLocators()
    {
        List<ServerLocation> snapshot;
        lock (_swapLock) { snapshot = [.. _locators]; }
        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(snapshot));
        return snapshot;
    }

    private static List<ServerLocation> Merge(
        IReadOnlyList<ServerLocation> serverList,
        IReadOnlyList<ServerLocation> clientList)
    {
        // cppcache ThinClientLocatorHelper.cpp:298-303 — preserve
        // client-known entries the server didn't echo back.
        var merged = new List<ServerLocation>(serverList);
        foreach (var oldLoc in clientList)
        {
            if (!merged.Contains(oldLoc))
            {
                merged.Add(oldLoc);
            }
        }
        return merged;
    }

    private void SwapLocators(List<ServerLocation> merged)
    {
        // cppcache: boost::unique_lock + locators_.swap(new_locators).
        // We mutate contents under the lock; readonly field can't have
        // its ref replaced, semantics are the same.
        lock (_swapLock)
        {
            _locators.Clear();
            _locators.AddRange(merged);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Frame builders
    // ─────────────────────────────────────────────────────────────

    private static byte[] BuildLocatorListRequestFrame(string serverGroup)
        => BuildRequestFrame(
            DSFid.LocatorListRequest,
            writer => new LocatorListRequest(serverGroup).WriteTo(writer));

    private static byte[] BuildClientConnectionRequestFrame(
        string serverGroup, IReadOnlyCollection<ServerLocation> excludeServers)
        => BuildRequestFrame(
            DSFid.ClientConnectionRequest,
            writer => new ClientConnectionRequest(serverGroup, excludeServers).WriteTo(writer));

    /// <summary>
    /// Common outer wrapping for every locator request: gossip version,
    /// Geode version ordinal, FixedIDByte envelope, then the body via
    /// <paramref name="writeBody"/>. Mirrors cppcache <c>sendRequest</c>
    /// (<c>ThinClientLocatorHelper.cpp:128-132</c>).
    /// </summary>
    /// <remarks>
    /// Locator DSFids in <see cref="DSFid"/> all fit in <see cref="sbyte"/>,
    /// so <see cref="DSCode.FixedIDByte"/> is the only envelope tag we
    /// need here. If a request type ever lands with a DSFid outside
    /// <c>[-128, 127]</c>, switch to <c>FixedIDShort</c>/<c>FixedIDInt</c>
    /// with the matching write width.
    /// </remarks>
    private static byte[] BuildRequestFrame(DSFid dsfid, Action<BigEndianBinaryWriter> writeBody)
    {
        var bufferWriter = new ArrayBufferWriter<byte>(64);
        var writer = new BigEndianBinaryWriter(bufferWriter);

        writer.WriteInt32(GossipVersion);
        // Ordinal MUST be int16 — Java TcpServer.processOneConnection reads
        // `input.readShort()` at TcpServer.java:413; an int32 here leaves
        // the trailing 2 bytes mis-aligning the DSCode/DSFid envelope and
        // the server rejects with
        // `UnsupportedSerializationVersionException: ordinal 0 not supported`.
        // Matches Java TcpClient.java:312 (`writeShort(ordinalVersion)`).
        writer.WriteInt16(ProtocolVersion.Current.Ordinal);
        writer.WriteByte(DSCode.FixedIDByte);
        writer.WriteSByte((sbyte)dsfid);
        writeBody(writer);

        return bufferWriter.WrittenSpan.ToArray();
    }

    // ─────────────────────────────────────────────────────────────
    //  Send / receive
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Send <paramref name="requestBytes"/> to <paramref name="loc"/>,
    /// verify the envelope, and decode the body via
    /// <paramref name="bodyDecoder"/>. Returns <see langword="null"/>
    /// on transport-level failures (caller advances to the next locator).
    /// Mirrors cppcache <c>sendRequest</c>
    /// (<c>ThinClientLocatorHelper.cpp:118-169</c>).
    /// </summary>
    private async Task<T?> TrySendAsync<T>(
        ServerLocation loc,
        byte[] requestBytes,
        DSFid expectedDsfid,
        Func<BigEndianBinaryReader, T> bodyDecoder,
        CancellationToken ct) where T : class
    {
        try
        {
            await using var conn = ActivatorUtilities.CreateInstance<LocatorConnection>(serviceProvider);
            await conn.ConnectAsync(loc.Host, loc.Port, ct).ConfigureAwait(false);
            await conn.SendAsync(requestBytes, ct).ConfigureAwait(false);

            // Response length is not pre-framed; grow the buffer
            // chunk-by-chunk and retry parsing after each read.
            // EndOfStreamException from BigEndianBinaryReader means
            // "need more bytes".
            var buffer = new byte[4096];
            var totalRead = 0;
            while (true)
            {
                try
                {
                    var reader = new BigEndianBinaryReader(buffer.AsMemory(0, totalRead));
                    ReadEnvelope(reader, expectedDsfid);
                    return bodyDecoder(reader);
                }
                catch (EndOfStreamException)
                {
                    // Need more bytes — fall through to the read below.
                }

                if (totalRead == buffer.Length)
                {
                    Array.Resize(ref buffer, buffer.Length * 2);
                }

                var n = await conn.ReadAsync(buffer.AsMemory(totalRead), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    logger.LogDebug(
                        "Locator [{Host}:{Port}] closed connection before complete response",
                        loc.Host, loc.Port);
                    return null;
                }
                totalRead += n;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            // SSL reject — propagate (no point trying other locators in
            // the same cluster, they almost certainly require SSL too).
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex,
                "Exception while querying locator [{Host}:{Port}]",
                loc.Host, loc.Port);
            return null;
        }
    }

    /// <summary>
    /// Consume the outer envelope: optional SSL-reject byte → DSCode
    /// <c>FixedIDByte</c> → DSFid sbyte. Throws on shape mismatch so
    /// the caller's catch-all reports it as a malformed locator
    /// response.
    /// </summary>
    private static void ReadEnvelope(BigEndianBinaryReader reader, DSFid expectedDsfid)
    {
        // cppcache: di.read() — if REPLY_SSL_ENABLED, throw; else rewind.
        // The byte serves dual purpose; we don't rewind, we just consume.
        var first = reader.ReadByte();
        if (first == ReplySslEnabled)
        {
            throw new NotSupportedException("Locator requires SSL; client TLS is not yet supported (Phase 3).");
        }
        if (first != DSCode.FixedIDByte)
        {
            throw new GeodeException($"Locator response: unexpected envelope DSCode {first} (expected FixedIDByte={DSCode.FixedIDByte}).");
        }

        var dsfid = (DSFid)reader.ReadSByte();
        if (dsfid != expectedDsfid)
        {
            throw new GeodeException($"Locator response: unexpected DSFid {dsfid} (expected {expectedDsfid}).");
        }
    }
}
