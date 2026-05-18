using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="double"/><c>[]</c> ↔
/// <see cref="DSCode.CacheableDoubleArray"/> (51). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 8 bytes big-endian
/// IEEE-754 per element. Mirrors cppcache <c>CacheableDoubleArray</c>
/// (<c>CacheableArrayPrimitive&lt;double, CacheableDoubleArray&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="DoubleDataConverter"/>
/// (DSCode 60) — NaN / ±Infinity round-trip preserves IEEE-754 bit
/// pattern. Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class DoubleArrayDataConverter(CacheScopeContext cacheScopeContext)
    : DataConverter<double[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableDoubleArray };

    private readonly int _maxArrayLength
        = cacheScopeContext.Options.Serialization.MaxArrayLength;

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, double[] value, byte dsCode, int depth)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"DoubleArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteDouble(element);
        }
    }

    public override double[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<double>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"DoubleArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) — refusing to allocate.");
        }
        var array = new double[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadDouble();
        }
        return array;
    }
}
