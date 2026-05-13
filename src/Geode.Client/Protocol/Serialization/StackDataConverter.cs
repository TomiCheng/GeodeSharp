using System.Collections;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>Stack&lt;T&gt;</c> ↔
/// <see cref="DSCode.CacheableStack"/> (74). Wire payload is the
/// standard collection shape — VL-encoded length followed by N
/// fully-serialised elements in <b>bottom-to-top</b> order (matching
/// Java <c>Stack</c>/<c>Vector</c>'s <c>elementData[0..N-1]</c> /
/// cppcache's <c>std::vector</c> backing). Mirrors
/// <c>clicache/src/CacheableStack.cpp::ToData</c> which writes via
/// <c>Linq::Enumerable::Reverse(stack)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order footgun.</b> <c>Stack&lt;T&gt;</c> in .NET enumerates
/// <i>top→bottom</i> (most recently pushed first); the wire expects
/// <i>bottom→top</i>. Write reverses, read does not. Symmetric.
/// Round-trip preserves the original push order — <c>Push(A); Push(B);
/// Push(C)</c> writes wire <c>[A, B, C]</c>, read pushes in wire order
/// so the rebuilt stack has <c>C</c> on top exactly as the original.
/// </para>
/// <para>
/// <b>Open-generic registration.</b> <see cref="ManagedType"/> returns
/// <c>typeof(Stack&lt;&gt;)</c>; the registry's <c>WriteObject</c>
/// dispatch falls back to <see cref="Type.GetGenericTypeDefinition"/>
/// when the closed-type lookup misses, so this single instance handles
/// every closed <c>Stack&lt;T&gt;</c>.
/// </para>
/// <para>
/// <b>Read returns canonical <c>Stack&lt;object?&gt;</c>.</b>
/// Target-shape conversion (to <c>Stack&lt;int&gt;</c>) happens at
/// <see cref="TypedResultAdapter"/>'s <c>Stack&lt;&gt;</c> branch,
/// which has to re-reverse the canonical's <i>top→bottom</i>
/// iteration before constructing the typed <c>Stack&lt;T&gt;</c>
/// via its <c>IEnumerable&lt;T&gt;</c> ctor (push-in-iteration-order
/// semantics).
/// </para>
/// </remarks>
internal sealed class StackDataConverter : IDataConverter
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableStack };

    private readonly SerializationRegistry _registry;

    public StackDataConverter(SerializationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public byte[] DsCodes => s_dsCodes;

    public Type ManagedType => typeof(Stack<>);

    public byte GetDsCode(object value) => DSCode.CacheableStack;

    public void Write(BigEndianBinaryWriter writer, object value, byte dsCode)
    {
        // Stack<T> implements non-generic ICollection — Count is
        // O(1), no scratch list needed.
        var source = (ICollection)value;
        writer.WriteArrayLen(source.Count);

        // Reverse the foreach output (top→bottom) into bottom→top for
        // wire. Single-pass copy into a scratch buffer descending,
        // then write the buffer ascending — same shape as clicache
        // CacheableStack::ToData's Linq Reverse but without the LINQ
        // chain.
        var buffer = new object?[source.Count];
        var i = source.Count - 1;
        foreach (var item in source)
        {
            buffer[i--] = item;
        }
        foreach (var item in buffer)
        {
            _registry.WriteObject(writer, item);
        }
    }

    public object? Read(BigEndianBinaryReader reader, byte dsCode)
    {
        var length = reader.ReadArrayLen();
        var stack = new Stack<object?>();
        if (length <= 0)
        {
            return stack;
        }

        // Wire is bottom→top order; pushing in wire order places
        // wire[0] at the bottom and wire[N-1] on top — original
        // push sequence preserved.
        for (var i = 0; i < length; i++)
        {
            stack.Push(_registry.ReadObject(reader));
        }
        return stack;
    }
}
