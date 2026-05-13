namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="short"/><c>[]</c> ↔
/// <see cref="DSCode.CacheableInt16Array"/> (47). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 2 bytes big-endian
/// per element. Mirrors cppcache <c>CacheableInt16Array</c>
/// (<c>CacheableArrayPrimitive&lt;int16_t, CacheableInt16Array&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="Int16DataConverter"/>
/// (DSCode 56). Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class Int16ArrayDataConverter : DataConverter<short[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableInt16Array };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, short[] value, byte dsCode)
    {
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteInt16(element);
        }
    }

    public override short[] Read(BigEndianBinaryReader reader, byte dsCode)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<short>();
        }
        var array = new short[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadInt16();
        }
        return array;
    }
}
