/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class BytesDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        // VL-encoded length 0 = single byte 0x00, no body.
        Assert.Equal(
            new byte[] { DSCode.CacheableBytes, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<byte>()));
    }

    [Fact]
    public void Encode_short_array_writes_dscode_and_inline_length()
    {
        // Length 3 fits in the 0..252 inline range → 1-byte length prefix.
        Assert.Equal(
            new byte[] { DSCode.CacheableBytes, 0x03, 0x01, 0x02, 0x03 },
            SerializationTestHelpers.Encode(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Encode_medium_array_writes_u16_length()
    {
        // Length 300 > 252 → 3-byte length prefix (0xFE + u16).
        var value = new byte[300];
        for (var i = 0; i < value.Length; i++) value[i] = (byte)i;

        var encoded = SerializationTestHelpers.Encode(value);

        Assert.Equal(DSCode.CacheableBytes, encoded[0]);
        Assert.Equal(0xFE, encoded[1]);                 // u16-length marker
        Assert.Equal(new byte[] { 0x01, 0x2C }, encoded[2..4]);   // u16 300
        Assert.Equal(1 + 3 + 300, encoded.Length);
        Assert.Equal(value, encoded[4..]);
    }

    [Fact]
    public void Encode_large_array_writes_i32_length()
    {
        // Length > 0xFFFF → 5-byte length prefix (0xFD + i32).
        var value = new byte[70000];

        var encoded = SerializationTestHelpers.Encode(value);

        Assert.Equal(DSCode.CacheableBytes, encoded[0]);
        Assert.Equal(0xFD, encoded[1]);                 // i32-length marker
        Assert.Equal(new byte[] { 0x00, 0x01, 0x11, 0x70 }, encoded[2..6]);  // i32 70000
        Assert.Equal(1 + 5 + 70000, encoded.Length);
    }

    [Fact]
    public void Decode_zero_length_returns_empty_array()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableBytes, 0x00 });
        Assert.Equal(Array.Empty<byte>(), result);
    }

    [Fact]
    public void Decode_inline_length_returns_byte_array()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableBytes, 0x03, 0xDE, 0xAD, 0xBE });
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE }, result);
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<byte>());
        Assert.Equal(Array.Empty<byte>(), result);
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0xFF, 0x00, 0x80, 0x7F })]    // signed-boundary mix
    public void RoundTrip_small(byte[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_252_byte_boundary()
    {
        // 252 = the highest 1-byte VL length.
        var value = new byte[252];
        for (var i = 0; i < value.Length; i++) value[i] = (byte)i;
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_253_byte_boundary()
    {
        // 253 = first length forcing the 3-byte VL prefix.
        var value = new byte[253];
        for (var i = 0; i < value.Length; i++) value[i] = (byte)i;
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_65536_byte_boundary()
    {
        // 65536 = first length forcing the 5-byte VL prefix.
        var value = new byte[65536];
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }
}

*/