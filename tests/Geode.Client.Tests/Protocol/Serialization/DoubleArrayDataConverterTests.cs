/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class DoubleArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableDoubleArray, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<double>()));
    }

    [Fact]
    public void Encode_writes_ieee754_be_per_element()
    {
        // 1.0 → 0x3FF0_0000_0000_0000, -1.0 → 0xBFF0_0000_0000_0000.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDoubleArray, 0x02,
                0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0xBF, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(new[] { 1.0, -1.0 }));
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<double>());
        Assert.Equal(Array.Empty<double>(), result);
    }

    [Theory]
    [InlineData(new[] { 0.0 })]
    [InlineData(new[] { 1.0, -1.0, 3.141592653589793 })]
    [InlineData(new[] { double.MinValue, double.MaxValue, double.Epsilon })]
    public void RoundTrip(double[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_preserves_nan_and_infinities()
    {
        var value = new[]
        {
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
        };
        var result = SerializationTestHelpers.RoundTrip(value);
        Assert.Equal(3, result.Length);
        Assert.True(double.IsNaN(result[0]));
        Assert.Equal(double.PositiveInfinity, result[1]);
        Assert.Equal(double.NegativeInfinity, result[2]);
    }
}

*/