/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class SingleDataConverterTests
{
    [Fact]
    public void Encode_zero_writes_dscode_and_four_zero_bytes()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableFloat, 0x00, 0x00, 0x00, 0x00 },
            SerializationTestHelpers.Encode(0f));
    }

    [Fact]
    public void Encode_one_writes_ieee754_big_endian()
    {
        // 1.0f = 0x3F800000.
        Assert.Equal(
            new byte[] { DSCode.CacheableFloat, 0x3F, 0x80, 0x00, 0x00 },
            SerializationTestHelpers.Encode(1.0f));
    }

    [Fact]
    public void Decode_reads_ieee754_value()
    {
        // -2.0f = 0xC0000000.
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableFloat, 0xC0, 0x00, 0x00, 0x00 });
        Assert.Equal(-2.0f, result);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.MinValue)]
    [InlineData(float.MaxValue)]
    [InlineData(float.Epsilon)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RoundTrip(float value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_negative_zero_preserves_sign_bit()
    {
        var result = SerializationTestHelpers.RoundTrip(-0.0f);
        // -0.0f == 0.0f under operator==, so compare via bit pattern.
        Assert.Equal(
            BitConverter.SingleToInt32Bits(-0.0f),
            BitConverter.SingleToInt32Bits(result));
    }

    [Fact]
    public void RoundTrip_nan_preserves_nan()
    {
        var result = SerializationTestHelpers.RoundTrip(float.NaN);
        Assert.True(float.IsNaN(result));
    }
}

*/