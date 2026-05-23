/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class SingleArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableFloatArray, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<float>()));
    }

    [Fact]
    public void Encode_writes_ieee754_be_per_element()
    {
        // 1.0f → 0x3F800000, -1.0f → 0xBF800000.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableFloatArray, 0x02,
                0x3F, 0x80, 0x00, 0x00,
                0xBF, 0x80, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(new[] { 1.0f, -1.0f }));
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<float>());
        Assert.Equal(Array.Empty<float>(), result);
    }

    [Theory]
    [InlineData(new[] { 0f })]
    [InlineData(new[] { 1f, -1f, 3.14159f })]
    [InlineData(new[] { float.MinValue, float.MaxValue, float.Epsilon })]
    public void RoundTrip(float[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_preserves_nan_and_infinities()
    {
        // BitConverter equality survives NaN / ±Infinity exactly; the
        // generic Assert.Equal float comparison treats NaN ≠ NaN, so
        // compare element-by-element with float.IsNaN where needed.
        var value = new[]
        {
            float.NaN,
            float.PositiveInfinity,
            float.NegativeInfinity,
        };
        var result = SerializationTestHelpers.RoundTrip(value);
        Assert.Equal(3, result.Length);
        Assert.True(float.IsNaN(result[0]));
        Assert.Equal(float.PositiveInfinity, result[1]);
        Assert.Equal(float.NegativeInfinity, result[2]);
    }
}

*/