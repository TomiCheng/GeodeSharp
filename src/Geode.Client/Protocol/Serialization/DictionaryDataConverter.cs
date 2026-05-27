using System.Collections;
using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>Dictionary&lt;K,V&gt;</c> /
/// <c>IDictionary&lt;K,V&gt;</c> ??/// <see cref="DSCode.CacheableHashMap"/> (67). Wire payload is a
/// VL-encoded entry count followed by N
/// <c>(key, value)</c> pairs ??each side a fully-serialised object
/// with its own DSCode. Mirrors cppcache <c>CacheableHashMap</c> +
/// the generic <c>writeObject(unordered_map)</c> in
/// <c>cppcache/include/geode/Serializer.hpp:338-348</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Open-generic registration.</b> <see cref="ManagedType"/> returns
/// <c>typeof(Dictionary&lt;,&gt;)</c>; the registry's <c>WriteObject</c>
/// dispatch falls back to <see cref="Type.GetGenericTypeDefinition"/>
/// when the closed-type lookup misses, so one instance covers every
/// closed <c>Dictionary&lt;K,V&gt;</c>.
/// </para>
/// <para>
/// <b>Key-value interleaved on the wire.</b> Entries are
/// <c>[k0, v0, k1, v1, ?�]</c> (cppcache calls <c>writeObject(key)</c>
/// then <c>writeObject(value)</c> per entry), NOT all-keys-then-all-
/// values. Read mirrors the order. Iteration order is non-
/// deterministic ??same as <c>std::unordered_map</c>.
/// </para>
/// <para>
/// <b>Read returns canonical <c>Dictionary&lt;object, object?&gt;</c>.</b>
/// Target-shape conversion (<c>Dictionary&lt;int, string&gt;</c>,
/// <c>IDictionary&lt;K,V&gt;</c>, ?? happens at
/// <see cref="TypedResultAdapter"/> in
/// <see cref="Regions.RegionView{TKey,TValue}"/>, not here.
/// </para>
/// <para>
/// <b>Null keys are rejected on read.</b> Java <c>HashMap</c> permits
/// one null key; <c>Dictionary&lt;object, object?&gt;</c> does not
/// (the underlying <see cref="object"/>-keyed Dictionary still throws
/// <c>ArgumentNullException</c> on a null key). If the wire ever
/// carries a null key (a Java-side <c>map.put(null, v)</c>) we throw
/// <see cref="GeodeException"/> with a descriptive message rather
/// than let Dictionary surface a generic argument-null error.
/// Null values are fine ??both sides allow that.
/// </para>
/// <para>
/// <b>Registry back-reference.</b> Same pattern as
/// <see cref="ListDataConverter"/> / <see cref="HashSetDataConverter"/>
/// ??each key + value re-enters
/// <see cref="SerializationRegistry.WriteObject"/> /
/// <see cref="SerializationRegistry.ReadObject"/> so nested maps /
/// lists / arbitrary registered types can occupy slots.
/// </para>
/// </remarks>
internal sealed class DictionaryDataConverter(
    SerializationRegistry serializationRegistry,
    SystemProperties systemProperties)
    : IDataConverter
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableHashMap };


    public byte[] DsCodes => _dsCodes;

    /// <summary>
    /// Open-generic <c>Dictionary&lt;,&gt;</c>. Registry's write
    /// dispatch reaches this converter via
    /// <see cref="Type.GetGenericTypeDefinition"/> when the closed-
    /// type lookup misses.
    /// </summary>
    public Type ManagedType => typeof(Dictionary<,>);

    public byte GetDsCode(object value) => DSCode.CacheableHashMap;

    public async ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct)
    {
        var source = (IDictionary)value;
        if (source.Count > systemProperties.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"DictionaryDataConverter: cannot serialise a map of {source.Count} entries "
                + $"— exceeds Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}).");
        }
        writer.WriteArrayLen(source.Count);
        foreach (DictionaryEntry entry in source)
        {
            await serializationRegistry.WriteObjectAsync(writer, entry.Key, depth + 1, ct);
            await serializationRegistry.WriteObjectAsync(writer, entry.Value, depth + 1, ct);
        }
    }

    public object? Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return new Dictionary<object, object?>();
        }
        if (length > systemProperties.MaxArrayLength)
        {
            throw new GeodeException(
                $"DictionaryDataConverter: wire map length {length} exceeds "
                + $"Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}) ??refusing to allocate.");
        }

        var dict = new Dictionary<object, object?>(capacity: length);
        for (var i = 0; i < length; i++)
        {
            var key = serializationRegistry.ReadObject(reader, depth + 1);
            var value = serializationRegistry.ReadObject(reader, depth + 1);

            if (key is null)
            {
                throw new GeodeException(
                    $"CacheableHashMap: wire entry #{i} has a null key; "
                    + "Java HashMap permits this but Dictionary<object, object?> does not.");
            }

            dict.Add(key, value);
        }
        return dict;
    }
}
