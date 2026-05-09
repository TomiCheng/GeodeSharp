using System.Buffers;
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class BigEndianBinaryWriterTests
{
    [Fact]
    public void WriteInt32_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt32(0x01020304);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteInt32_emits_negative_value_as_two_complement_big_endian()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt32(-1);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteInt64_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt64(0x0102030405060708L);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 },
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteInt16_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt16(0x0102);
        Assert.Equal(new byte[] { 0x01, 0x02 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteInt16_emits_negative_value_as_two_complement_big_endian()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt16(-1);
        Assert.Equal(new byte[] { 0xFF, 0xFF }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteUInt16_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteUInt16(0xABCD);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteUInt32_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteUInt32(0xDEADBEEFu);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteUInt64_emits_big_endian_bytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteUInt64(0x0102030405060708UL);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 },
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteFloat_emits_IEEE754_big_endian_bytes()
    {
        // 1.0f → 0x3F800000 in IEEE 754 single precision.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteFloat(1.0f);
        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteDouble_emits_IEEE754_big_endian_bytes()
    {
        // 1.0 → 0x3FF0000000000000 in IEEE 754 double precision.
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteDouble(1.0);
        Assert.Equal(
            new byte[] { 0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByte_emits_single_byte()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteByte(0xAB);
        Assert.Equal(new byte[] { 0xAB }, buffer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData((sbyte)0, 0x00)]
    [InlineData((sbyte)1, 0x01)]
    [InlineData((sbyte)127, 0x7F)]
    [InlineData((sbyte)-1, 0xFF)]
    [InlineData((sbyte)-128, 0x80)]
    public void WriteSByte_emits_two_complement_byte(sbyte value, byte expected)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteSByte(value);
        Assert.Equal(new byte[] { expected }, buffer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(true, 0x01)]
    [InlineData(false, 0x00)]
    public void WriteBool_emits_one_or_zero(bool value, byte expected)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteBool(value);
        Assert.Equal(new byte[] { expected }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteBytesOnly_emits_raw_bytes_without_length_prefix()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteBytesOnly(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Length_tracks_total_bytes_written()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        Assert.Equal(0, w.Length);
        w.WriteByte(0x01);
        Assert.Equal(1, w.Length);
        w.WriteInt32(0);
        Assert.Equal(5, w.Length);
        w.WriteInt64(0);
        Assert.Equal(13, w.Length);
    }

    [Fact]
    public void Multiple_writes_concatenate_in_order()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new BigEndianBinaryWriter(buffer);
        w.WriteInt32(0x01020304);
        w.WriteByte(0xFF);
        w.WriteBytesOnly(new byte[] { 0xAA, 0xBB });
        Assert.Equal(
            new byte[]
            {
                0x01, 0x02, 0x03, 0x04,
                0xFF,
                0xAA, 0xBB,
            },
            buffer.WrittenSpan.ToArray());
    }
}
