/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class ListDataConverterTests
{
    [Fact]
    public void Encode_empty_list_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableArrayList, 0x00 },
            SerializationTestHelpers.Encode(new List<int>()));
    }

    [Fact]
    public void Encode_int_elements_recurse_through_registry()
    {
        // {1,2,3} → DSCode 65 + length-3 + per-element (DSCode 57 + i32 BE).
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableArrayList, 0x03,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x02,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x03,
            },
            SerializationTestHelpers.Encode(new List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void Encode_string_elements_route_through_string_converter()
    {
        // ASCII elements pick DSCode 87 (CacheableASCIIString).
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableArrayList, 0x02,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,    // "A"
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x42,    // "B"
            },
            SerializationTestHelpers.Encode(new List<string> { "A", "B" }));
    }

    [Fact]
    public void Encode_null_element_routes_through_NullObj()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableArrayList, 0x02,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,    // "A"
                DSCode.NullObj,                                    // null slot
            },
            SerializationTestHelpers.Encode(new List<string?> { "A", null }));
    }

    [Fact]
    public void Decode_zero_length_returns_empty_list()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableArrayList, 0x00 });
        var typed = Assert.IsType<List<object?>>(result);
        Assert.Empty(typed);
    }

    [Fact]
    public void RoundTrip_returns_canonical_List_object()
    {
        // Wire format does not encode container element type — decode
        // always returns List<object?>. Target-shape conversion
        // (List<int> / IList<int> / …) happens at TypedResultAdapter,
        // not in this converter.
        var value = new List<int> { 1, 2, 3 };
        var decoded = SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value));
        var typed = Assert.IsType<List<object?>>(decoded);
        Assert.Equal(new object?[] { 1, 2, 3 }, typed);
    }

    [Fact]
    public void RoundTrip_with_null_elements_preserves_positions()
    {
        var value = new List<string?> { "a", null, "b", null };
        var decoded = (List<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(4, decoded.Count);
        Assert.Equal("a", decoded[0]);
        Assert.Null(decoded[1]);
        Assert.Equal("b", decoded[2]);
        Assert.Null(decoded[3]);
    }

    [Fact]
    public void RoundTrip_nested_lists()
    {
        // List<List<int>> → outer iterates, inner re-enters this same
        // converter via the open-generic fallback. Decoded shape is
        // List<object?> of List<object?>.
        var value = new List<List<int>>
        {
            new() { 1, 2 },
            new() { 3, 4 },
        };
        var decoded = (List<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(2, decoded.Count);
        var inner0 = Assert.IsType<List<object?>>(decoded[0]);
        var inner1 = Assert.IsType<List<object?>>(decoded[1]);
        Assert.Equal(new object?[] { 1, 2 }, inner0);
        Assert.Equal(new object?[] { 3, 4 }, inner1);
    }

    [Fact]
    public void RoundTrip_mixed_element_types_per_element_dispatch()
    {
        // Each element's runtime type drives its own converter — int,
        // string, bool, null all coexist in one List<object?>.
        var value = new List<object?> { 42, "hello", true, null };
        var decoded = (List<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(4, decoded.Count);
        Assert.Equal(42, decoded[0]);
        Assert.Equal("hello", decoded[1]);
        Assert.Equal(true, decoded[2]);
        Assert.Null(decoded[3]);
    }
}

*/