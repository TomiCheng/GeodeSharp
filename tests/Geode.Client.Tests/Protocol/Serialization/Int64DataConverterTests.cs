using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int64DataConverterTests
{
    [Fact]
    public void Encode_positive_writes_dscode_and_big_endian_bytes()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableInt64,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            },
            SerializationTestHelpers.Encode(0x0102030405060708L));
    }

    [Fact]
    public void Encode_negative_writes_two_complement()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableInt64,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            },
            SerializationTestHelpers.Encode(-1L));
    }

    [Fact]
    public void Decode_reads_signed_value()
    {
        var result = SerializationTestHelpers.Decode(new byte[]
        {
            DSCode.CacheableInt64,
            0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        });
        Assert.Equal(long.MinValue, result);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void RoundTrip(long value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
