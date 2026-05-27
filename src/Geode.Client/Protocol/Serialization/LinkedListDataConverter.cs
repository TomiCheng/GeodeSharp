using System.Collections;
using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>LinkedList&lt;T&gt;</c> ??/// <see cref="DSCode.CacheableLinkedList"/> (10). Wire payload is
/// identical to <see cref="ListDataConverter"/> ??VL-encoded length
/// followed by N fully-serialised elements ??because cppcache backs
/// both <c>CacheableArrayList</c> and <c>CacheableLinkedList</c> with
/// the same <c>std::vector&lt;CacheablePtr&gt;</c> (see
/// <c>cppcache/include/geode/CacheableBuiltins.hpp:348-358</c>). The
/// DSCode is what makes the server materialise a
/// <c>java.util.LinkedList</c> instead of a <c>java.util.ArrayList</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Open-generic registration.</b> <see cref="ManagedType"/> returns
/// <c>typeof(LinkedList&lt;&gt;)</c>; the registry's <c>WriteObject</c>
/// dispatch falls back to <see cref="Type.GetGenericTypeDefinition"/>
/// when the closed-type lookup misses, so this single instance handles
/// every closed <c>LinkedList&lt;T&gt;</c>.
/// </para>
/// <para>
/// <b>Not <c>IList&lt;T&gt;</c>-compatible.</b> Unlike
/// <c>List&lt;T&gt;</c>, <c>LinkedList&lt;T&gt;</c> only implements
/// <see cref="ICollection{T}"/> / <see cref="IReadOnlyCollection{T}"/>
/// ??it deliberately does <i>not</i> implement <see cref="IList{T}"/>
/// because indexed access is O(N) on a linked list. Callers wanting a
/// linked-list-shaped region value must declare
/// <c>IRegion&lt;K, LinkedList&lt;T&gt;&gt;</c>, not
/// <c>IRegion&lt;K, IList&lt;T&gt;&gt;</c>.
/// </para>
/// <para>
/// <b>Read returns canonical <c>LinkedList&lt;object?&gt;</c>.</b>
/// Target-shape conversion (to <c>LinkedList&lt;int&gt;</c>) happens
/// at <see cref="TypedResultAdapter"/>'s
/// <c>LinkedList&lt;&gt;</c> branch.
/// </para>
/// </remarks>
internal sealed class LinkedListDataConverter(
    SerializationRegistry serializationRegistry,
    SystemProperties systemProperties) : IDataConverter
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableLinkedList };

    public byte[] DsCodes => _dsCodes;

    public Type ManagedType => typeof(LinkedList<>);

    public byte GetDsCode(object value) => DSCode.CacheableLinkedList;

    public async ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct)
    {
        var source = (ICollection)value;
        if (source.Count > systemProperties.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"LinkedListDataConverter: cannot serialise a list of {source.Count} elements "
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
        var list = new LinkedList<object?>();
        if (length <= 0)
        {
            return list;
        }
        if (length > systemProperties.MaxArrayLength)
        {
            throw new GeodeException(
                $"LinkedListDataConverter: wire list length {length} exceeds "
                + $"Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}) ??refusing to allocate.");
        }

        for (var i = 0; i < length; i++)
        {
            // AddLast preserves wire order ??wire element 0 becomes
            // head, last element becomes tail.
            list.AddLast(serializationRegistry.ReadObject(reader, depth + 1));
        }
        return list;
    }
}
