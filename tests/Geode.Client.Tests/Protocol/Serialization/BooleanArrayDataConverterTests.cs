using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class BooleanArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.BooleanArray, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<bool>()));
    }

    [Fact]
    public void Encode_writes_one_byte_per_element_after_inline_length()
    {
        // {true, false, true} → DSCode + length-3 + 0x01 / 0x00 / 0x01.
        Assert.Equal(
            new byte[] { DSCode.BooleanArray, 0x03, 0x01, 0x00, 0x01 },
            SerializationTestHelpers.Encode(new[] { true, false, true }));
    }

    [Fact]
    public void Decode_zero_length_returns_empty_array()
    {
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.BooleanArray, 0x00 });
        Assert.Equal(Array.Empty<bool>(), result);
    }

    [Fact]
    public void Decode_treats_any_non_zero_byte_as_true()
    {
        // cppcache ReadBool: any non-zero byte is true. Server-side
        // values arrive normalised to 0x01, but be tolerant on read.
        var result = (bool[])SerializationTestHelpers.Decode(
            new byte[] { DSCode.BooleanArray, 0x02, 0xFF, 0x00 })!;
        Assert.Equal(new[] { true, false }, result);
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<bool>());
        Assert.Equal(Array.Empty<bool>(), result);
    }

    [Theory]
    [InlineData(new[] { true })]
    [InlineData(new[] { false })]
    [InlineData(new[] { true, false, true, true, false })]
    public void RoundTrip_small(bool[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
