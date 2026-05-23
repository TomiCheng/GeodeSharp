/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class BooleanDataConverterTests
{
    [Fact]
    public void Encode_true_writes_dscode_and_one_byte()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableBoolean, 0x01 },
            SerializationTestHelpers.Encode(true));
    }

    [Fact]
    public void Encode_false_writes_dscode_and_zero_byte()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableBoolean, 0x00 },
            SerializationTestHelpers.Encode(false));
    }

    [Fact]
    public void Decode_zero_byte_returns_false()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableBoolean, 0x00 });
        Assert.Equal(false, result);
    }

    [Fact]
    public void Decode_non_zero_byte_returns_true()
    {
        // Any non-zero byte is "true" per Java DataInput.readBoolean
        // semantics; we accept 0xFF the same as 0x01.
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableBoolean, 0xFF });
        Assert.Equal(true, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip(bool value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}

*/