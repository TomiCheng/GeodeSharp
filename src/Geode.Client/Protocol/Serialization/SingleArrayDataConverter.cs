using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="float"/><c>[]</c> ↔
/// <see cref="DSCode.CacheableFloatArray"/> (50). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 4 bytes big-endian
/// IEEE-754 per element. Mirrors cppcache <c>CacheableFloatArray</c>
/// (<c>CacheableArrayPrimitive&lt;float, CacheableFloatArray&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="SingleDataConverter"/>
/// (DSCode 59) — NaN / ±Infinity round-trip preserves IEEE-754 bit
/// pattern. Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class SingleArrayDataConverter(CacheScopeContext cacheScopeContext)
    : DataConverter<float[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableFloatArray };

    private readonly int _maxArrayLength
        = cacheScopeContext.Options.Serialization.MaxArrayLength;

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, float[] value, byte dsCode, int depth)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"SingleArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteFloat(element);
        }
    }

    public override float[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<float>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"SingleArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) — refusing to allocate.");
        }
        var array = new float[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadFloat();
        }
        return array;
    }
}
