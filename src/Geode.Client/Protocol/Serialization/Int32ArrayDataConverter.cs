using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="int"/><c>[]</c> ??/// <see cref="DSCode.CacheableInt32Array"/> (48). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 4 bytes big-endian
/// per element. Mirrors cppcache <c>CacheableInt32Array</c>
/// (<c>CacheableArrayPrimitive&lt;int32_t, CacheableInt32Array&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="Int32DataConverter"/>
/// (DSCode 57). Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class Int32ArrayDataConverter(SystemProperties systemProperties)
    : DataConverter<int[]>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableInt32Array };

    private readonly int _maxArrayLength
        = systemProperties.MaxArrayLength;

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, int[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"Int32ArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteInt32(element);
        }
        return ValueTask.CompletedTask;
    }

    public override int[] Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<int>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"Int32ArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) ??refusing to allocate.");
        }
        var array = new int[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadInt32();
        }
        return array;
    }
}
