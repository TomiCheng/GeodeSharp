using System.Collections;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>HashSet&lt;T&gt;</c> /
/// <c>ISet&lt;T&gt;</c> ??<see cref="DSCode.CacheableHashSet"/> (66).
/// Wire payload is a VL-encoded length followed by N fully-serialised
/// objects ??each element starts with its own DSCode byte. Mirrors
/// cppcache <c>CacheableHashSet</c> + the generic
/// <c>writeObject(unordered_set)</c> in
/// <c>cppcache/include/geode/Serializer.hpp:381-388</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Open-generic registration.</b> <see cref="ManagedType"/> returns
/// <c>typeof(HashSet&lt;&gt;)</c>; the registry's <c>WriteObject</c>
/// dispatch falls back to <see cref="Type.GetGenericTypeDefinition"/>
/// when the closed-type lookup misses, so this single instance handles
/// <c>HashSet&lt;int&gt;</c>, <c>HashSet&lt;string&gt;</c>, and every
/// other closed <c>HashSet&lt;T&gt;</c>.
/// </para>
/// <para>
/// <b>Read returns canonical <c>HashSet&lt;object?&gt;</c>.</b> Java's
/// wire format does not encode the container element type ??each slot
/// carries its own DSCode ??so target-shape conversion (to
/// <c>HashSet&lt;int&gt;</c>, <c>ISet&lt;string&gt;</c>, ?? happens
/// later at <see cref="TypedResultAdapter"/> in
/// <see cref="Regions.RegionView{TKey,TValue}"/>, not here. The
/// canonical decode keeps an <c>object?</c> element type so a null on
/// the wire survives the read (Java's <c>HashSet</c> permits one
/// null even though <c>std::unordered_set</c> does not).
/// </para>
/// <para>
/// <b>Count-before-iterate.</b> <c>HashSet&lt;T&gt;</c> deliberately
/// does <i>not</i> implement non-generic <see cref="ICollection"/>
/// (unlike <c>List&lt;T&gt;</c> / <c>Dictionary&lt;,&gt;</c>), so we
/// can't read <c>Count</c> off a type-erased cast. The wire format
/// puts the length first, so we collect once into a scratch
/// <c>List&lt;object?&gt;</c> to learn the count, then iterate that
/// list. One extra O(N) allocation; same trade-off as
/// <c>Enumerable.ToList</c>.
/// </para>
/// <para>
/// <b>Iteration order is non-deterministic</b> ??same as cppcache's
/// <c>std::unordered_set</c>. Round-trip equality must treat the wire
/// output as set-equal, not sequence-equal.
/// </para>
/// </remarks>
internal sealed class HashSetDataConverter : IDataConverter
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableHashSet };

    private readonly SerializationRegistry _registry;

    public HashSetDataConverter(SerializationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public byte[] DsCodes => s_dsCodes;

    /// <summary>
    /// Open-generic <c>HashSet&lt;&gt;</c>. Registry's write dispatch
    /// reaches this converter via
    /// <see cref="Type.GetGenericTypeDefinition"/> when the closed-type
    /// lookup for a concrete <c>HashSet&lt;T&gt;</c> misses.
    /// </summary>
    public Type ManagedType => typeof(HashSet<>);

    public byte GetDsCode(object value) => DSCode.CacheableHashSet;

    public void Write(DataOutput writer, object value, byte dsCode, int depth)
    {
        // HashSet<T> doesn't expose non-generic Count via cast; one
        // scratch pass collects the elements + counts them, second
        // pass writes them. Trade an O(N) alloc for one extra
        // enumeration over reflection on the typed Count property.
        var source = (IEnumerable)value;
        var items = new List<object?>();
        foreach (var item in source)
        {
            items.Add(item);
        }

        if (items.Count > _registry.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"HashSetDataConverter: cannot serialise a set of {items.Count} elements "
                + $"??exceeds Serialization.MaxArrayLength ({_registry.MaxArrayLength}).");
        }
        writer.WriteArrayLen(items.Count);
        foreach (var item in items)
        {
            // WriteObject handles null ??DSCode.NullObj and dispatches
            // by per-element runtime type.
            _registry.WriteObject(writer, item, depth + 1);
        }
    }

    public async ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct)
    {
        var source = (IEnumerable)value;
        var items = new List<object?>();
        foreach (var item in source) items.Add(item);

        if (items.Count > _registry.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"HashSetDataConverter: cannot serialise a set of {items.Count} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_registry.MaxArrayLength}).");
        }
        writer.WriteArrayLen(items.Count);
        foreach (var item in items)
        {
            await _registry.WriteObjectAsync(writer, item, depth + 1, ct);
        }
    }

    public object? Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return new HashSet<object?>();
        }
        if (length > _registry.MaxArrayLength)
        {
            throw new GeodeException(
                $"HashSetDataConverter: wire set length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_registry.MaxArrayLength}) ??refusing to allocate.");
        }

        var set = new HashSet<object?>(capacity: length);
        for (var i = 0; i < length; i++)
        {
            // Java permits one null in a HashSet; HashSet<object?>
            // mirrors that. Duplicate elements (whatever the wire
            // sends) are silently de-duplicated ??same semantics as
            // std::unordered_set::insert ignoring existing keys.
            set.Add(_registry.ReadObject(reader, depth + 1));
        }
        return set;
    }
}
