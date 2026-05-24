using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int16DataConverterTests
{
    [Fact]
    public void Encode_positive_writes_dscode_and_big_endian_bytes()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt16, 0x01, 0x02 },
            SerializationTestHelpers.Encode((short)0x0102));
    }

    [Fact]
    public void Encode_negative_writes_two_complement()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt16, 0xFF, 0xFF },
            SerializationTestHelpers.Encode((short)-1));
    }

    [Fact]
    public void Decode_reads_signed_value()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableInt16, 0x80, 0x00 });
        Assert.Equal(short.MinValue, result);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)-1)]
    [InlineData(short.MinValue)]
    [InlineData(short.MaxValue)]
    public void RoundTrip(short value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
