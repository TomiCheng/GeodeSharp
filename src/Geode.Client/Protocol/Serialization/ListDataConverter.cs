using System.Collections;
using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>List&lt;T&gt;</c> /
/// <c>IList&lt;T&gt;</c> ??<see cref="DSCode.CacheableArrayList"/> (65).
/// Wire payload is a VL-encoded length followed by N fully-serialised
/// objects ??each element starts with its own DSCode byte (including
/// <see cref="DSCode.NullObj"/> for nulls). Mirrors cppcache
/// <c>CacheableArrayList</c>
/// (<c>cppcache/src/CacheableArrayList.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Open-generic registration.</b> <see cref="ManagedType"/> returns
/// <c>typeof(List&lt;&gt;)</c>; the registry's <c>WriteObject</c>
/// dispatch falls back to <see cref="Type.GetGenericTypeDefinition"/>
/// when the closed-type lookup misses, so this single instance handles
/// <c>List&lt;int&gt;</c>, <c>List&lt;string&gt;</c>, and every other
/// closed <c>List&lt;T&gt;</c>.
/// </para>
/// <para>
/// <b>Read returns canonical <c>List&lt;object?&gt;</c>.</b> Java's
/// wire format does not encode the container element type ??each slot
/// carries its own DSCode ??so target-shape conversion happens later
/// at <see cref="TypedResultAdapter"/> in
/// <see cref="Regions.RegionView{TKey,TValue}"/>, not here.
/// </para>
/// <para>
/// <b><c>null</c> elements</b> survive the round trip: the registry
/// emits <see cref="DSCode.NullObj"/> for any null passed to
/// <see cref="SerializationRegistry.WriteObject"/>, and decodes that
/// DSCode back to <c>null</c>. A top-level <c>null</c> list is
/// intercepted by the registry one level higher and never reaches this
/// converter.
/// </para>
/// <para>
/// <b>Registry back-reference.</b> Same pattern as
/// <see cref="ObjectArrayDataConverter"/> /
/// <see cref="StringArrayDataConverter"/>: each element re-enters
/// <see cref="SerializationRegistry.WriteObject"/> /
/// <see cref="SerializationRegistry.ReadObject"/> so any registered
/// type (including nested lists / arrays) can occupy a slot. Safe
/// <c>this</c> pass at registry construction ??we store the reference
/// but only invoke through it later, by which point the registry is
/// fully populated.
/// </para>
/// </remarks>
internal sealed class ListDataConverter(
    SerializationRegistry serializationRegistry,
    SystemProperties systemProperties) : IDataConverter
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableArrayList };

    public byte[] DsCodes => _dsCodes;

    /// <summary>
    /// Open-generic <c>List&lt;&gt;</c>. The registry's write dispatch
    /// reaches this converter via <see cref="Type.GetGenericTypeDefinition"/>
    /// when the closed-type lookup for a concrete <c>List&lt;T&gt;</c>
    /// misses.
    /// </summary>
    public Type ManagedType => typeof(List<>);

    public byte GetDsCode(object value) => DSCode.CacheableArrayList;

    public async ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct)
    {
        var source = (IList)value;
        if (source.Count > systemProperties.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"ListDataConverter: cannot serialise a list of {source.Count} elements "
                + $"— exceeds Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}).");
        }
        writer.WriteArrayLen(source.Count);
        foreach (var item in source)
        {
            await serializationRegistry.WriteObjectAsync(writer, item, depth + 1, ct);
        }
    }

    public object? Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return new List<object?>(0);
        }
        if (length > systemProperties.MaxArrayLength)
        {
            throw new GeodeException(
                $"ListDataConverter: wire list length {length} exceeds "
                + $"Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}) ??refusing to allocate.");
        }

        var list = new List<object?>(length);
        for (var i = 0; i < length; i++)
        {
            // Each slot's DSCode is read by ReadObject. Null elements
            // come back as null via DSCode.NullObj. Any registered
            // type (including a nested ArrayList) is a valid slot.
            list.Add(serializationRegistry.ReadObject(reader, depth + 1));
        }
        return list;
    }
}
