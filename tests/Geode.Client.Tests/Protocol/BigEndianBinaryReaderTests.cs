using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class BigEndianBinaryReaderTests
{
    [Fact]
    public void ReadInt32_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        Assert.Equal(0x01020304, r.ReadInt32());
    }

    [Fact]
    public void ReadInt32_decodes_negative_value()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        Assert.Equal(-1, r.ReadInt32());
    }

    [Fact]
    public void ReadInt64_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[]
        {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        });
        Assert.Equal(0x0102030405060708L, r.ReadInt64());
    }

    [Fact]
    public void ReadByte_decodes_single_byte()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xAB });
        Assert.Equal(0xAB, r.ReadByte());
    }

    [Theory]
    [InlineData((byte)0x00, false)]
    [InlineData((byte)0x01, true)]
    [InlineData((byte)0xFF, true)]   // any non-zero is "true" — symmetric with Java
    public void ReadBool_treats_zero_as_false_anything_else_as_true(byte raw, bool expected)
    {
        var r = new BigEndianBinaryReader(new byte[] { raw });
        Assert.Equal(expected, r.ReadBool());
    }

    [Fact]
    public void ReadBytesOnly_returns_zero_copy_slice()
    {
        var source = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
        var r = new BigEndianBinaryReader(source);

        Assert.Equal(0x10, r.ReadByte());          // advance past first byte
        var slice = r.ReadBytesOnly(3);
        Assert.Equal(new byte[] { 0x20, 0x30, 0x40 }, slice.ToArray());

        // Mutating the underlying source mutates the slice — proves zero-copy.
        source[2] = 0xFF;
        Assert.Equal(0xFF, slice.Span[1]);
    }

    [Fact]
    public void ReadBytesOnly_with_negative_count_throws()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0x01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => r.ReadBytesOnly(-1));
    }

    [Fact]
    public void ReadInt32_past_end_throws_EndOfStreamException()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0x01, 0x02 });
        Assert.Throws<EndOfStreamException>(() => r.ReadInt32());
    }

    [Fact]
    public void Position_and_Remaining_track_correctly()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        Assert.Equal(0, r.Position);
        Assert.Equal(5, r.Remaining);

        r.ReadByte();
        Assert.Equal(1, r.Position);
        Assert.Equal(4, r.Remaining);

        r.ReadInt32();
        Assert.Equal(5, r.Position);
        Assert.Equal(0, r.Remaining);
    }
}
