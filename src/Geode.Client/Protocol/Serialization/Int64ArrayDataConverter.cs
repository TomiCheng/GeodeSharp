using Geode.Client.Internal;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="long"/><c>[]</c> ??/// <see cref="DSCode.CacheableInt64Array"/> (49). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by 8 bytes big-endian
/// per element. Mirrors cppcache <c>CacheableInt64Array</c>
/// (<c>CacheableArrayPrimitive&lt;int64_t, CacheableInt64Array&gt;</c>).
/// </summary>
/// <remarks>
/// Per-element wire shape matches <see cref="Int64DataConverter"/>
/// (DSCode 58). Same key / null / empty rules as
/// <see cref="BooleanArrayDataConverter"/>.
/// </remarks>
internal sealed class Int64ArrayDataConverter(GeodeCache cache)
    : DataConverter<long[]>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableInt64Array };

    private readonly int _maxArrayLength
        = cache.CacheProperties.MaxArrayLength;

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, long[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"Int64ArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteInt64(element);
        }
        return ValueTask.CompletedTask;
    }

    public override long[] Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<long>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"Int64ArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) ??refusing to allocate.");
        }
        var array = new long[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadInt64();
        }
        return array;
    }
}
