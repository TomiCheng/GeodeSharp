using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class HashSetDataConverterTests
{
    [Fact]
    public void Encode_empty_set_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableHashSet, 0x00 },
            SerializationTestHelpers.Encode(new HashSet<int>()));
    }

    [Fact]
    public void Encode_single_int_element_predictable_bytes()
    {
        // Single element → deterministic wire (order question doesn't
        // apply when N=1). DSCode 66 + length 1 + (DSCode 57 + i32 BE).
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableHashSet, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,
            },
            SerializationTestHelpers.Encode(new HashSet<int> { 7 }));
    }

    [Fact]
    public void Encode_single_string_routes_through_string_converter()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableHashSet, 0x01,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,    // "A"
            },
            SerializationTestHelpers.Encode(new HashSet<string> { "A" }));
    }

    [Fact]
    public void Decode_zero_length_returns_empty_canonical_set()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableHashSet, 0x00 });
        var typed = Assert.IsType<HashSet<object?>>(result);
        Assert.Empty(typed);
    }

    [Fact]
    public void RoundTrip_returns_canonical_HashSet_object()
    {
        // Wire format does not encode container element type — decode
        // always returns HashSet<object?>. Target-shape conversion to
        // HashSet<int> happens at TypedResultAdapter, not here.
        var value = new HashSet<int> { 1, 2, 3 };
        var decoded = SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value));
        var typed = Assert.IsType<HashSet<object?>>(decoded);
        // Set-equal comparison — wire iteration order is non-
        // deterministic (cppcache unordered_set).
        Assert.Equal(
            new HashSet<object?> { 1, 2, 3 },
            typed);
    }

    [Fact]
    public void RoundTrip_with_null_element_survives()
    {
        // Java HashSet permits one null; HashSet<object?> mirrors
        // that. cppcache's unordered_set doesn't but the wire wraps
        // null via DSCode.NullObj uniformly.
        var value = new HashSet<string?> { "a", null, "b" };
        var decoded = (HashSet<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Contains("a", decoded);
        Assert.Contains("b", decoded);
        Assert.Contains(null, decoded);
    }

    [Fact]
    public void RoundTrip_string_set()
    {
        var value = new HashSet<string> { "alpha", "beta", "gamma" };
        var decoded = (HashSet<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Contains("alpha", decoded);
        Assert.Contains("beta", decoded);
        Assert.Contains("gamma", decoded);
    }

    [Fact]
    public void RoundTrip_mixed_element_types_per_element_dispatch()
    {
        // Each element's runtime type drives its own converter.
        var value = new HashSet<object> { 42, "hello", true };
        var decoded = (HashSet<object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Contains(42, decoded);
        Assert.Contains("hello", decoded);
        Assert.Contains(true, decoded);
    }
}
