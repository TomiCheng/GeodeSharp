namespace Geode.Client.Protocol;

internal sealed record TcrPart(byte IsObject, ReadOnlyMemory<byte> Payload)
{
    /// <summary>
    /// Serialise this Part onto <paramref name="writer"/>.
    /// </summary>
    public void Encode(DataOutput writer)
    {
        writer.WriteInt32(Payload.Length);
        writer.WriteByte(IsObject);
        writer.WriteBytesOnly(Payload.Span);
    }

    public static TcrPart Decode(DataInput reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new FormatException(
                $"TcrPart length must be non-negative, got {length}.");
        }
        var isObject = reader.ReadByte();
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
