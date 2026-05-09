using System.Buffers;

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
/// instead (parts first to learn their byte length, then header + parts) —
/// simpler given our writer does not expose a seek/patch API. The output
/// bytes are identical.
/// </para>
/// </remarks>
internal sealed record TcrMessage(
    MessageType MessageType,
    int TransactionId,
    byte EarlyAck,
    IReadOnlyList<TcrPart> Parts)
{
    /// <summary>Fixed-size frame header: four i32 fields + one u8.</summary>
    public const int HeaderLength = 17;

    /// <summary>Encode this message to a freshly-allocated byte array.</summary>
    public byte[] Encode()
    {
        // Pass 1: encode parts to learn their total byte length.
        var partsBuffer = new ArrayBufferWriter<byte>();
        var partsWriter = new BigEndianBinaryWriter(partsBuffer);
        foreach (var part in Parts)
        {
            part.Encode(partsWriter);
        }
        var partsBytes = partsBuffer.WrittenSpan;

        // Pass 2: write header followed by the parts payload.
        var buffer = new ArrayBufferWriter<byte>(HeaderLength + partsBytes.Length);
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt32((int)MessageType);
        w.WriteInt32(partsBytes.Length);   // MessageLength = bytes occupied by Parts
        w.WriteInt32(Parts.Count);
        w.WriteInt32(TransactionId);
        w.WriteByte(EarlyAck);
        w.WriteBytesOnly(partsBytes);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decode one message from <paramref name="bytes"/>.</summary>
    /// <exception cref="FormatException">
    /// The frame is malformed (negative <c>NumParts</c> or
    /// <c>MessageLength</c> disagrees with the bytes occupied by the parts).
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// The buffer is shorter than the frame claims.
    /// </exception>
    public static TcrMessage Decode(ReadOnlyMemory<byte> bytes)
    {
        var reader = new BigEndianBinaryReader(bytes);

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

        return new TcrMessage(messageType, transactionId, earlyAck, parts);
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
}
