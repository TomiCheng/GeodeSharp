using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class DoubleDataConverterTests
{
    [Fact]
    public void Encode_zero_writes_dscode_and_eight_zero_bytes()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDouble,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(0d));
    }

    [Fact]
    public void Encode_one_writes_ieee754_big_endian()
    {
        // 1.0d = 0x3FF0000000000000.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDouble,
                0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(1.0d));
    }

    [Fact]
    public void Decode_reads_ieee754_value()
    {
        // -2.0d = 0xC000000000000000.
        var result = SerializationTestHelpers.Decode(new byte[]
        {
            DSCode.CacheableDouble,
            0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        });
        Assert.Equal(-2.0d, result);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1.0d)]
    [InlineData(-1.0d)]
    [InlineData(double.MinValue)]
    [InlineData(double.MaxValue)]
    [InlineData(double.Epsilon)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RoundTrip(double value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_negative_zero_preserves_sign_bit()
    {
        var result = SerializationTestHelpers.RoundTrip(-0.0d);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(-0.0d),
            BitConverter.DoubleToInt64Bits(result));
    }

    [Fact]
    public void RoundTrip_nan_preserves_nan()
    {
        var result = SerializationTestHelpers.RoundTrip(double.NaN);
        Assert.True(double.IsNaN(result));
    }
}
