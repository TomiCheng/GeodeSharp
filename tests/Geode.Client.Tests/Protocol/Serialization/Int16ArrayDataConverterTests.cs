using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int16ArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt16Array, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<short>()));
    }

    [Fact]
    public void Encode_writes_i16_be_per_element()
    {
        // {0x0102, -1} → length-2 + 0x01 0x02 / 0xFF 0xFF.
        Assert.Equal(
            new byte[] { DSCode.CacheableInt16Array, 0x02, 0x01, 0x02, 0xFF, 0xFF },
            SerializationTestHelpers.Encode(new short[] { 0x0102, -1 }));
    }

    [Fact]
    public void Decode_reads_signed_values()
    {
        var result = (short[])SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableInt16Array, 0x02, 0x80, 0x00, 0x7F, 0xFF })!;
        Assert.Equal(new short[] { short.MinValue, short.MaxValue }, result);
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<short>());
        Assert.Equal(Array.Empty<short>(), result);
    }

    [Theory]
    [InlineData(new short[] { 0 })]
    [InlineData(new short[] { 1, 2, 3 })]
    [InlineData(new short[] { short.MinValue, -1, 0, 1, short.MaxValue })]
    public void RoundTrip(short[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
