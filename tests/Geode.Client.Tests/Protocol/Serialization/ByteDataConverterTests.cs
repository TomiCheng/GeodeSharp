/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class ByteDataConverterTests
{
    [Fact]
    public void Encode_zero_writes_dscode_and_zero()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableByte, 0x00 },
            SerializationTestHelpers.Encode((byte)0));
    }

    [Fact]
    public void Encode_max_writes_dscode_and_FF()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableByte, 0xFF },
            SerializationTestHelpers.Encode(byte.MaxValue));
    }

    [Fact]
    public void Decode_byte_preserves_full_u8_range()
    {
        // .NET byte=255 ↔ Java byte=-1 share wire 0xFF; round-trip
        // through .NET is value-preserving even at the unsigned/signed
        // boundary because we never interpret as signed.
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableByte, 0xFF });
        Assert.Equal((byte)255, result);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)127)]     // .NET = Java 127
    [InlineData((byte)128)]     // .NET = 128, Java = -128 — bit pattern 0x80
    [InlineData((byte)255)]     // .NET = 255, Java = -1
    public void RoundTrip(byte value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}

*/