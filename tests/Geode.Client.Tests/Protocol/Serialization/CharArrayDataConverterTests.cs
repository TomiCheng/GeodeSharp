using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class CharArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CharArray, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<char>()));
    }

    [Fact]
    public void Encode_writes_u16_be_per_ascii_char()
    {
        // {'A','B'} → DSCode + length-2 + 0x0041 / 0x0042.
        Assert.Equal(
            new byte[] { DSCode.CharArray, 0x02, 0x00, 0x41, 0x00, 0x42 },
            SerializationTestHelpers.Encode(new[] { 'A', 'B' }));
    }

    [Fact]
    public void Encode_writes_u16_be_for_cjk_code_unit()
    {
        // '中' = U+4E2D → wire 0x4E 0x2D.
        Assert.Equal(
            new byte[] { DSCode.CharArray, 0x01, 0x4E, 0x2D },
            SerializationTestHelpers.Encode(new[] { '中' }));
    }

    [Fact]
    public void Decode_reads_chars_back_in_order()
    {
        var result = (char[])SerializationTestHelpers.Decode(
            new byte[] { DSCode.CharArray, 0x02, 0x00, 0x41, 0x4E, 0x2D })!;
        Assert.Equal(new[] { 'A', '中' }, result);
    }

    [Theory]
    [InlineData(new[] { '\0' })]                  // NUL code unit survives
    [InlineData(new[] { 'A', 'B', 'C' })]
    [InlineData(new[] { '中', '文' })]
    [InlineData(new[] { '￿' })]              // max u16 code unit
    public void RoundTrip(char[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
}
