using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="short"/><c>[]</c> ??/// <see cref="DSCode.CacheableInt16Array"/> (47). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 2 bytes big-endian
/// per element. Mirrors cppcache <c>CacheableInt16Array</c>
/// (<c>CacheableArrayPrimitive&lt;int16_t, CacheableInt16Array&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="Int16DataConverter"/>
/// (DSCode 56). Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class Int16ArrayDataConverter(SystemProperties systemProperties)
    : DataConverter<short[]>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableInt16Array };

    private readonly int _maxArrayLength
        = systemProperties.MaxArrayLength;

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, short[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"Int16ArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteInt16(element);
        }
        return ValueTask.CompletedTask;
    }

    public override short[] Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<short>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"Int16ArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) ??refusing to allocate.");
        }
        var array = new short[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadInt16();
        }
        return array;
    }
}
