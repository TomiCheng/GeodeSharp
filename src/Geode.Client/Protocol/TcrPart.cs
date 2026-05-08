namespace Geode.Client.Protocol;

/// <summary>
/// One TCR message Part on the wire: <c>i32 length</c> + <c>u8 IsObject</c>
/// + raw payload bytes. Wire-format building block only; semantics of the
/// payload (typed value, region name, serialised object, etc.) live in
/// higher layers.
/// </summary>
/// <remarks>
/// Mirrors the inline 3-step encoding used throughout
/// <c>cppcache/src/TcrMessage.cpp</c> (<c>writeBytePart</c>,
/// <c>writeIntPart</c>, <c>writeRegionPart</c>, ...): every typed helper
/// there writes <c>i32 length + i8 isObject + payload</c>.
///
/// Equality is content-based: two <see cref="TcrPart"/> values with the
/// same <see cref="IsObject"/> flag and the same payload bytes compare
/// equal regardless of which underlying buffer they slice into.
/// </remarks>
internal sealed record TcrPart(bool IsObject, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Serialise this Part onto <paramref name="writer"/>.</summary>
    public void Encode(BigEndianBinaryWriter writer)
    {
        writer.WriteInt32(Payload.Length);
        writer.WriteBool(IsObject);
        writer.WriteBytesOnly(Payload.Span);
    }

    /// <summary>Read one Part from <paramref name="reader"/>.</summary>
    /// <exception cref="FormatException">
    /// The decoded length is negative.
    /// </exception>
    /// <exception cref="EndOfStreamException">
    /// The reader does not contain enough bytes for the encoded length.
    /// </exception>
    public static TcrPart Decode(BigEndianBinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new FormatException(
                $"TcrPart length must be non-negative, got {length}.");
        }
        var isObject = reader.ReadBool();
        var payload = reader.ReadBytesOnly(length);
        return new TcrPart(isObject, payload);
    }

    public bool Equals(TcrPart? other) =>
        other is not null
        && IsObject == other.IsObject
        && Payload.Span.SequenceEqual(other.Payload.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IsObject);
        hash.AddBytes(Payload.Span);
        return hash.ToHashCode();
    }
}
