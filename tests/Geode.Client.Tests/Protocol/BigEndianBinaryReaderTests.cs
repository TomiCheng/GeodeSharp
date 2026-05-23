/*
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
    public void ReadInt16_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0x01, 0x02 });
        Assert.Equal(0x0102, r.ReadInt16());
    }

    [Fact]
    public void ReadInt16_decodes_negative_value()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xFF, 0xFF });
        Assert.Equal((short)-1, r.ReadInt16());
    }

    [Fact]
    public void ReadUInt16_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xAB, 0xCD });
        Assert.Equal((ushort)0xABCD, r.ReadUInt16());
    }

    [Fact]
    public void ReadUInt32_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
    }

    [Fact]
    public void ReadUInt64_decodes_big_endian_bytes()
    {
        var r = new BigEndianBinaryReader(new byte[]
        {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        });
        Assert.Equal(0x0102030405060708UL, r.ReadUInt64());
    }

    [Fact]
    public void ReadFloat_decodes_IEEE754_big_endian_bytes()
    {
        // 0x3F800000 → 1.0f.
        var r = new BigEndianBinaryReader(new byte[] { 0x3F, 0x80, 0x00, 0x00 });
        Assert.Equal(1.0f, r.ReadFloat());
    }

    [Fact]
    public void ReadDouble_decodes_IEEE754_big_endian_bytes()
    {
        // 0x3FF0000000000000 → 1.0.
        var r = new BigEndianBinaryReader(new byte[]
        {
            0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        });
        Assert.Equal(1.0, r.ReadDouble());
    }

    [Fact]
    public void ReadByte_decodes_single_byte()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xAB });
        Assert.Equal(0xAB, r.ReadByte());
    }

    [Theory]
    [InlineData(0x00, (sbyte)0)]
    [InlineData(0x01, (sbyte)1)]
    [InlineData(0x7F, (sbyte)127)]
    [InlineData(0xFF, (sbyte)-1)]
    [InlineData(0x80, (sbyte)-128)]
    public void ReadSByte_decodes_two_complement_byte(byte raw, sbyte expected)
    {
        var r = new BigEndianBinaryReader(new byte[] { raw });
        Assert.Equal(expected, r.ReadSByte());
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

    // ====================================================================
    //  ReadArrayLen — variable-length array length encoding
    // ====================================================================

    [Theory]
    [InlineData(new byte[] { 0x00 }, 0)]
    [InlineData(new byte[] { 0x01 }, 1)]
    [InlineData(new byte[] { 0x7F }, 127)]   // i8 boundary — must still be unsigned
    [InlineData(new byte[] { 0x80 }, 128)]   // first byte that goes negative as sbyte
    [InlineData(new byte[] { 0xFB }, 251)]
    [InlineData(new byte[] { 0xFC }, 252)]   // top of inline range
    public void ReadArrayLen_inline_byte_is_unsigned(byte[] wire, int expected)
    {
        // Inline-length wire byte was originally read as signed, which
        // mis-decoded 0x80..0xFC as -128..-4. The byte must be read
        // unsigned to match the writer (WriteByte((byte)length)).
        var r = new BigEndianBinaryReader(wire);
        Assert.Equal(expected, r.ReadArrayLen());
    }

    [Fact]
    public void ReadArrayLen_u16_marker_reads_two_more_bytes()
    {
        // 0xFE + u16 BE 0x012C = 300.
        var r = new BigEndianBinaryReader(new byte[] { 0xFE, 0x01, 0x2C });
        Assert.Equal(300, r.ReadArrayLen());
    }

    [Fact]
    public void ReadArrayLen_i32_marker_reads_four_more_bytes()
    {
        // 0xFD + i32 BE 0x00011170 = 70000.
        var r = new BigEndianBinaryReader(new byte[] { 0xFD, 0x00, 0x01, 0x11, 0x70 });
        Assert.Equal(70000, r.ReadArrayLen());
    }

    [Fact]
    public void ReadArrayLen_null_sentinel_returns_minus_one()
    {
        var r = new BigEndianBinaryReader(new byte[] { 0xFF });
        Assert.Equal(-1, r.ReadArrayLen());
    }
}

*/