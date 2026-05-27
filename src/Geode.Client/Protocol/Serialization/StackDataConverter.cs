using System.Collections;
using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <c>Stack&lt;T&gt;</c> ??/// <see cref="DSCode.CacheableStack"/> (74). Wire payload is the
/// standard collection shape ??VL-encoded length followed by N
/// fully-serialised elements in <b>bottom-to-top</b> order (matching
/// Java <c>Stack</c>/<c>Vector</c>'s <c>elementData[0..N-1]</c> /
/// cppcache's <c>std::vector</c> backing). Mirrors
/// <c>clicache/src/CacheableStack.cpp::ToData</c> which writes via
/// <c>Linq::Enumerable::Reverse(stack)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order footgun.</b> <c>Stack&lt;T&gt;</c> in .NET enumerates
/// <i>top?�bottom</i> (most recently pushed first); the wire expects
/// <i>bottom?�top</i>. Write reverses, read does not. Symmetric.
/// Round-trip preserves the original push order ??<c>Push(A); Push(B);
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
/// which has to re-reverse the canonical's <i>top?�bottom</i>
/// iteration before constructing the typed <c>Stack&lt;T&gt;</c>
/// via its <c>IEnumerable&lt;T&gt;</c> ctor (push-in-iteration-order
/// semantics).
/// </para>
/// </remarks>
internal sealed class StackDataConverter(
    SerializationRegistry serializationRegistry,
    SystemProperties systemProperties) : IDataConverter
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableStack };

    public byte[] DsCodes => _dsCodes;

    public Type ManagedType => typeof(Stack<>);

    public byte GetDsCode(object value) => DSCode.CacheableStack;

    public async ValueTask WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct)
    {
        var source = (ICollection)value;
        if (source.Count > systemProperties.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"StackDataConverter: cannot serialise a stack of {source.Count} elements "
                + $"— exceeds Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}).");
        }
        writer.WriteArrayLen(source.Count);

        var buffer = new object?[source.Count];
        var idx = source.Count - 1;
        foreach (var item in source)
        {
            buffer[idx--] = item;
        }
        foreach (var item in buffer)
        {
            await serializationRegistry.WriteObjectAsync(writer, item, depth + 1, ct);
        }
    }

    public object? Read(DataInput reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        var stack = new Stack<object?>();
        if (length <= 0)
        {
            return stack;
        }
        if (length > systemProperties.MaxArrayLength)
        {
            throw new GeodeException(
                $"StackDataConverter: wire stack length {length} exceeds "
                + $"Serialization.MaxArrayLength ({systemProperties.MaxArrayLength}) ??refusing to allocate.");
        }

        // Wire is bottom?�top order; pushing in wire order places
        // wire[0] at the bottom and wire[N-1] on top ??original
        // push sequence preserved.
        for (var i = 0; i < length; i++)
        {
            stack.Push(serializationRegistry.ReadObject(reader, depth + 1));
        }
        return stack;
    }
}
