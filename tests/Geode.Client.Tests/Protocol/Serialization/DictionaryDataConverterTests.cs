/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class DictionaryDataConverterTests
{
    [Fact]
    public void Encode_empty_map_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableHashMap, 0x00 },
            SerializationTestHelpers.Encode(new Dictionary<int, string>()));
    }

    [Fact]
    public void Encode_single_entry_interleaves_key_then_value()
    {
        // Single entry → deterministic wire. DSCode 67 + length 1 +
        // (key DSCode 57 + i32 BE) + (value DSCode 87 + u16 len + 'A').
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableHashMap, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x05,        // key 5
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,        // value "A"
            },
            SerializationTestHelpers.Encode(
                new Dictionary<int, string> { [5] = "A" }));
    }

    [Fact]
    public void Encode_null_value_routes_through_NullObj()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableHashMap, 0x01,
                DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x05,        // key
                DSCode.NullObj,                                        // null value
            },
            SerializationTestHelpers.Encode(
                new Dictionary<int, string?> { [5] = null }));
    }

    [Fact]
    public void Decode_zero_length_returns_empty_canonical_dict()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableHashMap, 0x00 });
        var typed = Assert.IsType<Dictionary<object, object?>>(result);
        Assert.Empty(typed);
    }

    [Fact]
    public void Decode_null_key_throws_GeodeException()
    {
        // Java HashMap permits one null key; Dictionary<object,
        // object?> does not. Hand-craft a wire payload that puts
        // DSCode.NullObj in the key slot and assert the converter
        // refuses with a clear message rather than ArgumentNullException
        // from Dictionary.Add.
        var nullKeyWire = new byte[]
        {
            DSCode.CacheableHashMap, 0x01,
            DSCode.NullObj,                                            // null key
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,             // value 7
        };

        var ex = Assert.Throws<GeodeException>(
            () => SerializationTestHelpers.Decode(nullKeyWire));
        Assert.Contains("null key", ex.Message);
    }

    [Fact]
    public void RoundTrip_returns_canonical_Dictionary_object_object()
    {
        // Wire format does not encode K/V type. Decode always returns
        // Dictionary<object, object?>; TypedResultAdapter handles the
        // shape conversion afterwards.
        var value = new Dictionary<int, string>
        {
            [1] = "alpha",
            [2] = "beta",
        };
        var decoded = SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value));
        var typed = Assert.IsType<Dictionary<object, object?>>(decoded);
        // Order non-deterministic; assert pair-equal.
        Assert.Equal(2, typed.Count);
        Assert.Equal("alpha", typed[1]);
        Assert.Equal("beta", typed[2]);
    }

    [Fact]
    public void RoundTrip_with_null_value_preserved()
    {
        var value = new Dictionary<int, string?>
        {
            [1] = "a",
            [2] = null,
            [3] = "c",
        };
        var decoded = (Dictionary<object, object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Equal("a", decoded[1]);
        Assert.Null(decoded[2]);
        Assert.Equal("c", decoded[3]);
    }

    [Fact]
    public void RoundTrip_mixed_key_and_value_types()
    {
        // Each key + value runs its runtime-type converter
        // independently. Heterogeneous Dictionary<object, object?>
        // survives the wire.
        var value = new Dictionary<object, object?>
        {
            ["k1"] = 42,
            [2] = "v2",
            [true] = false,
        };
        var decoded = (Dictionary<object, object?>)SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;
        Assert.Equal(3, decoded.Count);
        Assert.Equal(42, decoded["k1"]);
        Assert.Equal("v2", decoded[2]);
        Assert.Equal(false, decoded[true]);
    }
}

*/