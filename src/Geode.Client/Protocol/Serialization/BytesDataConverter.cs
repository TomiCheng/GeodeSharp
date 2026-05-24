using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="byte"/><c>[]</c> ??/// <see cref="DSCode.CacheableBytes"/> (46). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes, see
/// <see cref="DataOutput.WriteArrayLen"/>) followed by
/// the raw bytes. Mirrors cppcache <c>CacheableBytes</c>
/// (<c>cppcache/include/geode/internal/CacheableBuiltinTemplates.hpp</c>
/// <c>CacheableArrayPrimitive&lt;int8_t, CacheableBytes&gt;</c>) which
/// routes through <c>serializer::writeArrayObject</c> ??/// <c>writeArrayLen(size) + per-byte writeObject(int8_t)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a Key.</b> cppcache's <c>CacheableArrayPrimitive</c> derives
/// from <c>DataSerializablePrimitive</c> only, NOT
/// <c>CacheableKey</c> ??Java <c>Arrays.equals</c> / <c>Arrays.hashCode</c>
/// are array-content semantics that don't match the per-class
/// <c>operator==</c> / <c>hashcode()</c> contract <c>CacheableKey</c>
/// requires. .NET enforces the same exclusion at compile time: the
/// <c>where TKey : IEquatable&lt;TKey&gt;</c> constraint on
/// <see cref="IRegion{TKey, TValue}"/> rejects <see cref="byte"/><c>[]</c>
/// because <see cref="System.Array"/> does not implement
/// <see cref="IEquatable{T}"/>. So <see cref="byte"/><c>[]</c>
/// values work; <see cref="byte"/><c>[]</c> keys don't compile.
/// </para>
/// <para>
/// <b><c>null</c> vs <see cref="Array.Empty{T}"/>():</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <c>null</c> is intercepted by
///     <see cref="SerializationRegistry.WriteObject"/> ahead of the
///     converter and written as <see cref="DSCode.NullObj"/> (41).
///     This converter never sees a null write input.
///   </item>
///   <item>
///     <see cref="Array.Empty{T}"/>() writes
///     <c>[46, 0x00]</c> ??DSCode + VL-encoded length 0, no payload.
///     Read returns a (possibly fresh) zero-length array, not null.
///   </item>
/// </list>
/// </remarks>
internal sealed class BytesDataConverter(GeodeCache cache)
    : DataConverter<byte[]>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableBytes };

    private readonly int _maxBytesLength
        = cache.CacheProperties.MaxBytesLength;

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, byte[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxBytesLength)
        {
            throw new InvalidOperationException(
                $"BytesDataConverter: cannot serialise a byte[] of {value.Length} bytes "
                + $"— exceeds Serialization.MaxBytesLength ({_maxBytesLength}).");
        }
        writer.WriteBytes(value);
        return ValueTask.CompletedTask;
    }

    public override byte[]? Read(DataInput reader, byte dsCode, int depth)
    {
        // Inline the length read so we can bounds-check before
        // allocating. reader.ReadBytes() does the same two steps
        // internally; we just split them to insert the limit gate.
        var length = reader.ReadArrayLen();
        if (length == -1) return null;
        if (length > _maxBytesLength)
        {
            throw new GeodeException(
                $"BytesDataConverter: wire byte[] length {length} exceeds "
                + $"Serialization.MaxBytesLength ({_maxBytesLength}) ??refusing to allocate.");
        }
        return reader.ReadBytesOnly(length).ToArray();
    }
}
