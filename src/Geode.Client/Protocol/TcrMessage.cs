
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

/// <summary>
/// One TCR (Thin-Client Request / Response) message frame on the wire.
/// </summary>
/// <remarks>
/// <para>Wire layout (all multi-byte fields big-endian):</para>
/// <code>
/// offset  0 : i32  MessageType
/// offset  4 : i32  MessageLength      // bytes occupied by the Parts (header excluded)
/// offset  8 : i32  NumParts
/// offset 12 : i32  TransactionId
/// offset 16 : u8   EarlyAck            // bit-flags (security, retry, ...)
/// offset 17 : Part[NumParts]           // each Part = i32 len + u8 isObject + payload
/// </code>
/// <para>
/// Mirrors <c>TcrMessage::writeHeader</c> /
/// <c>TcrMessage::handleByteArrayResponse</c> /
/// <c>TcrMessage::writeMessageLength</c> in
/// <c>cppcache/src/TcrMessage.cpp</c>.
/// </para>
/// <para>
/// Cppcache writes a dummy <c>0</c> for <c>MessageLength</c> at encode time
/// and patches offset 4 once the parts are written. We use a two-pass encode
/// instead (parts first to learn their byte length, then header + parts) ??/// simpler given our writer does not expose a seek/patch API. The output
/// bytes are identical.
/// </para>
/// </remarks>
internal sealed record TcrMessage(
    IServiceProvider ServiceProvider,
    MessageType MessageType,
    int TransactionId,
    byte EarlyAck,
    IReadOnlyList<TcrPart> Parts)
{

    /// <summary>Fixed-size frame header: four i32 fields + one u8.</summary>
    public const int HeaderLength = 17;

    /// <summary>
    /// <see cref="EarlyAck"/> bit set by <see cref="UpdateHeaderForRetry"/>
    /// to flag a resent message; cppcache <c>TcrMessage::updateHeaderForRetry</c>
    /// ORs <c>0x4</c> into the EarlyAck byte so the server can dedupe.
    /// </summary>
    private const byte IsRetryBit = 0x4;

    /// <summary>
    /// Return a copy with the retry bit set on <see cref="EarlyAck"/>, so
    /// the server's <c>ClientHealthMonitor</c> can dedupe a resent op
    /// against a prior attempt that may have made it through.
    /// </summary>
    /// <remarks>
    /// Mirrors cppcache <c>TcrMessage::updateHeaderForRetry</c>
    /// (<c>cppcache/src/TcrMessage.cpp:805-809</c>). cppcache patches the
    /// already-encoded byte buffer in place; we return a new record since
    /// <see cref="TcrMessage"/> is immutable and re-encodes on demand.
    /// </remarks>
    public TcrMessage UpdateHeaderForRetry() =>
        this with { EarlyAck = (byte)(EarlyAck | IsRetryBit) };

    /// <summary>
    /// Server-supplied version tag attached to a Reply. Collapses cppcache's
    /// <c>m_versionTag</c> member + <c>getVersionTag</c> / <c>setVersionTag</c>
    /// (<c>cppcache/src/TcrMessage.cpp:303-308</c>, <c>.hpp:412</c>) into one
    /// C# auto-property. Default <see langword="null"/> mirrors cppcache's
    /// default-constructed member (<c>TcrMessage.cpp:147</c>); the reply-decode
    /// version-tag read path (cppcache <c>readVersionTag</c>,
    /// <c>TcrMessage.cpp:407-416</c>) sets it once wired. Phase 1.x doesn't
    /// drive concurrency checks, so it stays <see langword="null"/> — a reply
    /// that carried no tag — and consumers (e.g.
    /// <c>ThinClientRegion.PutNoThrowRemoteAsync</c>) propagate that null.
    /// </summary>
    public VersionTag? VersionTag { get; set; }

    /// <summary>Encode this message to a freshly-allocated byte array.</summary>
    public byte[] Encode()
    {
        using var partsWriter = ActivatorUtilities.CreateInstance<DataOutput>(ServiceProvider);
        foreach (var part in Parts)
        {
            part.Encode(partsWriter);
        }
        var partsBytes = partsWriter.WrittenSpan;

        using var w = ActivatorUtilities.CreateInstance<DataOutput>(ServiceProvider);
        w.WriteInt32((int)MessageType);
        w.WriteInt32(partsBytes.Length);
        w.WriteInt32(Parts.Count);
        w.WriteInt32(TransactionId);
        w.WriteByte(EarlyAck);
        w.WriteBytesOnly(partsBytes);
        return w.WrittenSpan.ToArray();
    }

    /// <summary>Decode one message from <paramref name="bytes"/>.</summary>
    /// <exception cref="FormatException">
    /// The frame is malformed (negative <c>NumParts</c> or
    /// <c>MessageLength</c> disagrees with the bytes occupied by the parts).
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// The buffer is shorter than the frame claims.
    /// </exception>
    public static TcrMessage Decode(IServiceProvider serviceProvider, ReadOnlyMemory<byte> bytes)
    {
        var reader = new DataInput(bytes);

        var messageType = (MessageType)reader.ReadInt32();
        var messageLength = reader.ReadInt32();
        var numParts = reader.ReadInt32();
        var transactionId = reader.ReadInt32();
        var earlyAck = reader.ReadByte();

        if (numParts < 0)
        {
            throw new FormatException(
                $"NumParts must be non-negative, got {numParts}.");
        }

        var parts = new List<TcrPart>(numParts);
        var partsStart = reader.Position;
        for (var i = 0; i < numParts; i++)
        {
            parts.Add(TcrPart.Decode(reader));
        }
        var partsConsumed = reader.Position - partsStart;

        if (partsConsumed != messageLength)
        {
            throw new FormatException(
                $"Header MessageLength={messageLength} does not match the {partsConsumed} bytes consumed by the parts.");
        }

        return ActivatorUtilities.CreateInstance<TcrMessage>(
            serviceProvider, messageType, transactionId, earlyAck, parts);
    }

    public bool Equals(TcrMessage? other)
    {
        if (other is null) return false;
        if (MessageType != other.MessageType) return false;
        if (TransactionId != other.TransactionId) return false;
        if (EarlyAck != other.EarlyAck) return false;
        if (Parts.Count != other.Parts.Count) return false;
        for (var i = 0; i < Parts.Count; i++)
        {
            if (!Parts[i].Equals(other.Parts[i])) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MessageType);
        hash.Add(TransactionId);
        hash.Add(EarlyAck);
        foreach (var part in Parts)
        {
            hash.Add(part);
        }
        return hash.ToHashCode();
    }

    ///// <summary>
    ///// The server-side exception text carried by an
    ///// <see cref="MessageType.Exception"/> reply (the Java exception's
    ///// fully-qualified class name + message), used by the dispatcher to
    ///// classify failures (e.g. auth-required retry).
    ///// </summary>
    ///// <remarks>
    ///// Mirrors <c>TcrMessage::getException</c>
    ///// (<c>cppcache/src/TcrMessage.cpp:213</c>), which lazily stringifies
    ///// <c>m_value</c> (the deserialized exception payload). NIE stub
    ///// until exception-reply deserialization lands.
    ///// </remarks>
    //public string GetException() =>
    //    throw new NotImplementedException(
    //        "Phase 3 ??TcrMessage.GetException (exception-reply payload stringify)");

    ///// <summary>
    ///// True when <paramref name="msg"/> is a user-initiated region op
    ///// (Put / Get / Query / register-interest / ...) rather than a
    ///// framework-internal control frame (PING, PERIODIC_ACK,
    ///// CLOSE_CONNECTION, CLIENT_READY, PDX / CQ / metadata fetches, ...).
    ///// </summary>
    ///// <remarks>
    ///// Mirrors <c>TcrMessage::isUserInitiativeOps</c>
    ///// (<c>cppcache/src/TcrMessage.cpp:98</c>). Only the multi-user /
    ///// security dispatch path consults this predicate ??Phase 3 work,
    ///// hence NIE stub until then.
    ///// </remarks>
    //public static bool IsUserInitiativeOps(TcrMessage msg) =>
    //    throw new NotImplementedException(
    //        "Phase 3 ??TcrMessage.IsUserInitiativeOps (auth / multi-user dispatch)");
}
