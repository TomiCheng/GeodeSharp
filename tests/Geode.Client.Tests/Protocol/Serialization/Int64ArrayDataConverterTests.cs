using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int64ArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt64Array, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<long>()));
    }

    [Fact]
    public void Encode_writes_i64_be_per_element()
    {
        // {1, -1} → length-2 + 8 bytes per element BE.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableInt64Array, 0x02,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            },
            SerializationTestHelpers.Encode(new long[] { 1, -1 }));
    }

    [Fact]
    public void Decode_reads_signed_min_max()
    {
        var result = (long[])SerializationTestHelpers.Decode(
            new byte[]
            {
                DSCode.CacheableInt64Array, 0x02,
                0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // long.MinValue
                0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,   // long.MaxValue
            })!;
        Assert.Equal(new[] { long.MinValue, long.MaxValue }, result);
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<long>());
        Assert.Equal(Array.Empty<long>(), result);
    }

    [Theory]
    [InlineData(new long[] { 0 })]
    [InlineData(new long[] { 1, 2, 3 })]
    [InlineData(new long[] { long.MinValue, -1, 0, 1, long.MaxValue })]
    public void RoundTrip(long[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
