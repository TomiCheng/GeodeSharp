/*
using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="bool"/><c>[]</c> ??/// <see cref="DSCode.BooleanArray"/> (26). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes; see
/// <see cref="DataOutput.WriteArrayLen"/>) followed by
/// one byte per element (<c>0</c> = false, <c>0x01</c> = true).
/// Mirrors cppcache <c>BooleanArray</c>
/// (<c>cppcache/src/CacheableBuiltins.cpp</c> typedef of
/// <c>CacheableArrayPrimitive&lt;bool, BooleanArray&gt;</c>) which
/// routes through <c>serializer::writeArrayObject</c> ??/// <c>writeArrayLen(size) + per-element writeObject(bool)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="byte"/><c>[]</c> (DSCode 46) on the wire:
/// same length prefix + 1-byte-per-element shape, but the server
/// materialises this as Java <c>boolean[]</c> rather than
/// <c>byte[]</c>. Encoder bug that wrote into the wrong DSCode would
/// be silently round-trip-equivalent on the client and only surface
/// when a Java consumer reads it.
/// </para>
/// <para>
/// <b>Not a Key.</b> Same reasoning as <see cref="BytesDataConverter"/>
/// ??<see cref="System.Array"/> doesn't implement
/// <see cref="IEquatable{T}"/>, so <see cref="IRegion{TKey, TValue}"/>'s
/// <c>where TKey : IEquatable&lt;TKey&gt;</c> constraint rejects
/// <see cref="bool"/><c>[]</c> keys at compile time. Values are fine.
/// </para>
/// <para>
/// <c>null</c> is intercepted by
/// <see cref="SerializationRegistry.WriteObject"/> ahead of this
/// converter and emitted as <see cref="DSCode.NullObj"/>; an empty
/// array writes <c>[26, 0x00]</c> (DSCode + length 0) and reads back
/// as <see cref="Array.Empty{T}"/>.
/// </para>
/// </remarks>
internal sealed class BooleanArrayDataConverter(CacheScopeContext cacheScopeContext)
    : DataConverter<bool[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.BooleanArray };

    /// <summary>
    /// Snapshot of <see cref="Options.SerializationOptions.MaxArrayLength"/>
    /// at construction. The per-cache options bag is one-shot
    /// (<see cref="CacheScopeContext.Initialize"/> runs before any
    /// consumer resolves) so caching the value avoids a property-chain
    /// walk on every wire op.
    /// </summary>
    private readonly int _maxArrayLength
        = cacheScopeContext.Options.Serialization.MaxArrayLength;

    public override byte[] DsCodes => s_dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, bool[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxArrayLength)
        {
            throw new InvalidOperationException(
                $"BooleanArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_maxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteBool(element);
        }
        return ValueTask.CompletedTask;
    }

    public override bool[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<bool>();
        }
        if (length > _maxArrayLength)
        {
            throw new GeodeException(
                $"BooleanArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_maxArrayLength}) ??refusing to allocate. "
                + "Treat as a hostile / buggy payload unless a legitimate workload "
                + "warrants raising the limit.");
        }
        var array = new bool[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = reader.ReadBool();
        }
        return array;
    }
}

*/