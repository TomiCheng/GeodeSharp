namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="long"/><c>[]</c> ↔
/// <see cref="DSCode.CacheableInt64Array"/> (49). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 8 bytes big-endian
/// per element. Mirrors cppcache <c>CacheableInt64Array</c>
/// (<c>CacheableArrayPrimitive&lt;int64_t, CacheableInt64Array&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="Int64DataConverter"/>
/// (DSCode 58). Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class Int64ArrayDataConverter : DataConverter<long[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableInt64Array };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, long[] value, byte dsCode, int depth)
    {
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteInt64(element);
        }
    }

    public override long[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<long>();
        }
        var array = new long[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadInt64();
        }
        return array;
    }
}
