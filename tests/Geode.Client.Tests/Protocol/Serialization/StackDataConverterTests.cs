/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class StackDataConverterTests
{
    [Fact]
    public void Encode_empty_stack_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableStack, 0x00 },
            SerializationTestHelpers.Encode(new Stack<int>()));
    }

    [Fact]
    public void Encode_reverses_to_bottom_to_top_wire_order()
    {
        // Push 1, 2, 3 → top=3. Wire must be [1, 2, 3] in
        // bottom-to-top order so the server's java.util.Stack lands
        // with 1 at the bottom and 3 on top. .NET Stack<T> iterates
        // top→bottom natively (yields 3, 2, 1); the converter reverses
        // that. Mirrors clicache CacheableStack::ToData calling
        // Linq::Enumerable::Reverse(stack).
        var stack = new Stack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);

        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableStack, 0x03,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x01,    // bottom
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x02,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x03,    // top
            },
            SerializationTestHelpers.Encode(stack));
    }

    [Fact]
    public void Decode_reads_bottom_to_top_and_pushes_in_wire_order()
    {
        // Wire [1, 2, 3] (bottom-to-top) → Push(1); Push(2); Push(3)
        // → final stack has 1 at bottom, 3 on top.
        var wire = new byte[]
        {
            DSCode.CacheableStack, 0x03,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x01,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x02,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x03,
        };

        var typed = (Stack<object?>)SerializationTestHelpers.Decode(wire)!;
        Assert.Equal(3, typed.Count);
        Assert.Equal(3, typed.Peek());          // top = last-pushed
        Assert.Equal(3, typed.Pop());
        Assert.Equal(2, typed.Pop());
        Assert.Equal(1, typed.Pop());
    }

    [Fact]
    public void Decode_zero_length_returns_empty_canonical_stack()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableStack, 0x00 });
        var typed = Assert.IsType<Stack<object?>>(result);
        Assert.Empty(typed);
    }

    [Fact]
    public void RoundTrip_returns_canonical_Stack_object()
    {
        // Push order A, B, C → top=C. Round trip must preserve that:
        // peek = C, pop = C → B → A.
        var value = new Stack<int>();
        value.Push(10);
        value.Push(20);
        value.Push(30);

        var decoded = SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value));

        var typed = Assert.IsType<Stack<object?>>(decoded);
        Assert.Equal(30, typed.Peek());
        Assert.Equal(30, typed.Pop());
        Assert.Equal(20, typed.Pop());
        Assert.Equal(10, typed.Pop());
    }

    [Fact]
    public void RoundTrip_single_element()
    {
        // N=1 is the degenerate case where reverse is a no-op; check
        // the trivial path still works.
        var value = new Stack<string>();
        value.Push("only");

        var decoded = (Stack<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Single(decoded);
        Assert.Equal("only", decoded.Peek());
    }

    [Fact]
    public void RoundTrip_with_null_element()
    {
        var value = new Stack<string?>();
        value.Push("a");
        value.Push(null);
        value.Push("b");

        var decoded = (Stack<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Equal("b", decoded.Pop());
        Assert.Null(decoded.Pop());
        Assert.Equal("a", decoded.Pop());
    }
}

*/