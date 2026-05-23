/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class LinkedListDataConverterTests
{
    [Fact]
    public void Encode_empty_list_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableLinkedList, 0x00 },
            SerializationTestHelpers.Encode(new LinkedList<int>()));
    }

    [Fact]
    public void Encode_int_elements_preserve_head_to_tail_order()
    {
        // Wire is deterministic — head→tail iteration matches
        // ArrayList layout (cppcache backs both with std::vector).
        var list = new LinkedList<int>();
        list.AddLast(1);
        list.AddLast(2);
        list.AddLast(3);

        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableLinkedList, 0x03,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x02,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x03,
            },
            SerializationTestHelpers.Encode(list));
    }

    [Fact]
    public void Encode_null_element_routes_through_NullObj()
    {
        var list = new LinkedList<string?>();
        list.AddLast("A");
        // AddLast(null) is ambiguous between AddLast(T) and
        // AddLast(LinkedListNode<T>) — cast to disambiguate.
        list.AddLast((string?)null);

        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableLinkedList, 0x02,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,
                DSCode.NullObj,
            },
            SerializationTestHelpers.Encode(list));
    }

    [Fact]
    public void Decode_zero_length_returns_empty_canonical_list()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableLinkedList, 0x00 });
        var typed = Assert.IsType<LinkedList<object?>>(result);
        Assert.Empty(typed);
    }

    [Fact]
    public void RoundTrip_returns_canonical_LinkedList_object()
    {
        var value = new LinkedList<int>();
        value.AddLast(10);
        value.AddLast(20);
        value.AddLast(30);

        var decoded = SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value));

        // Canonical decode is LinkedList<object?>, NOT LinkedList<int>
        // — shape conversion happens at TypedResultAdapter.
        var typed = Assert.IsType<LinkedList<object?>>(decoded);
        Assert.Equal(new object?[] { 10, 20, 30 }, typed);   // head→tail order
    }

    [Fact]
    public void RoundTrip_preserves_null_element_position()
    {
        var value = new LinkedList<string?>();
        value.AddLast("a");
        value.AddLast((string?)null);
        value.AddLast("b");

        var decoded = (LinkedList<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;

        var array = decoded.ToArray();
        Assert.Equal(3, array.Length);
        Assert.Equal("a", array[0]);
        Assert.Null(array[1]);
        Assert.Equal("b", array[2]);
    }
}

*/