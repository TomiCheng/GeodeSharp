/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int32DataConverterTests
{
    [Fact]
    public void Encode_positive_writes_dscode_and_big_endian_bytes()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt32, 0x01, 0x02, 0x03, 0x04 },
            SerializationTestHelpers.Encode(0x01020304));
    }

    [Fact]
    public void Encode_negative_writes_two_complement()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt32, 0xFF, 0xFF, 0xFF, 0xFF },
            SerializationTestHelpers.Encode(-1));
    }

    [Fact]
    public void Decode_reads_signed_value()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableInt32, 0x80, 0x00, 0x00, 0x00 });
        Assert.Equal(int.MinValue, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void RoundTrip(int value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}

*/