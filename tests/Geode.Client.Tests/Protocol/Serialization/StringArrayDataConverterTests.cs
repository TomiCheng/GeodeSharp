using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class StringArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableStringArray, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<string>()));
    }

    [Fact]
    public void Encode_ascii_element_routes_through_CacheableASCIIString()
    {
        // {"A"} → length-1 + (DSCode 87 + u16 length 1 + 'A').
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableStringArray, 0x01,
                DSCode.CacheableASCIIString, 0x00, 0x01, 0x41,
            },
            SerializationTestHelpers.Encode(new[] { "A" }));
    }

    [Fact]
    public void Encode_non_ascii_element_routes_through_CacheableString_modUtf8()
    {
        // {"中"} → length-1 + (DSCode 42 + u16 byte-length 3 + 0xE4 0xB8 0xAD).
        // U+4E2D in Java modified UTF-8 is the same 3-byte sequence as
        // standard UTF-8 (mod-UTF-8 only diverges for NUL and
        // supplementary code points).
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableStringArray, 0x01,
                DSCode.CacheableString, 0x00, 0x03, 0xE4, 0xB8, 0xAD,
            },
            SerializationTestHelpers.Encode(new[] { "中" }));
    }

    [Fact]
    public void Encode_null_element_routes_through_NullObj()
    {
        // {null} → length-1 + DSCode 41. No per-element body.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableStringArray, 0x01,
                DSCode.NullObj,
            },
            SerializationTestHelpers.Encode(new string?[] { null }));
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<string>());
        Assert.Equal(Array.Empty<string>(), result);
    }

    [Fact]
    public void RoundTrip_ascii_elements()
    {
        var value = new[] { "alpha", "beta", "gamma" };
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_mixed_ascii_and_cjk()
    {
        // Forces the registry to pick different per-element DSCodes
        // (87 for "Hello", 42 for the CJK element).
        var value = new[] { "Hello", "中文", "World" };
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_with_null_elements_preserves_positions()
    {
        var value = new string?[] { "a", null, "b", null };
        var result = (string?[])SerializationTestHelpers.Decode(
            SerializationTestHelpers.Encode(value))!;

        Assert.Equal(4, result.Length);
        Assert.Equal("a", result[0]);
        Assert.Null(result[1]);
        Assert.Equal("b", result[2]);
        Assert.Null(result[3]);
    }
}
