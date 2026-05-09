using System.Buffers.Binary;

namespace Geode.Client.Protocol;

/// <summary>
/// Sequential big-endian reader over an in-memory buffer.
/// C# counterpart of cppcache <c>DataInput</c> / <c>java.io.DataInput</c>:
/// every multi-byte primitive is decoded from network byte order, matching
/// what a Geode server sends.
/// </summary>
/// <remarks>
/// Not thread-safe. Single consumer, read-only. Throws
/// <see cref="EndOfStreamException"/> when a read would go past the end of
/// the buffer.
///
/// BCL's <c>System.IO.BinaryReader</c> is little-endian, hence the explicit
/// "BigEndian" prefix on this type — do not confuse the two.
///
/// Methods marked "prototype" throw <see cref="NotImplementedException"/>
/// and will be filled in as later phases need them.
/// </remarks>
internal sealed class BigEndianBinaryReader(ReadOnlyMemory<byte> buffer)
{
    private int _position;

    /// <summary>Current byte offset within the buffer.</summary>
    public int Position => _position;

    /// <summary>Total length of the underlying buffer.</summary>
    public int Length => buffer.Length;

    /// <summary>Bytes left to read from the current <see cref="Position"/>.</summary>
    public int Remaining => buffer.Length - _position;

    // ======================================================================
    //  Implemented (Phase 1 — frame codec)
    // ======================================================================

    /// <summary>Read a single unsigned byte (u8).</summary>
    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        var value = buffer.Span[_position];
        _position += sizeof(byte);
        return value;
    }

    /// <summary>Read a single byte and interpret it as a boolean (0 = false, anything else = true).</summary>
    public bool ReadBool() => ReadByte() != 0;

    /// <summary>Read a 32-bit signed integer in big-endian byte order.</summary>
    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        var value = BinaryPrimitives.ReadInt32BigEndian(buffer.Span.Slice(_position, sizeof(int)));
        _position += sizeof(int);
        return value;
    }

    /// <summary>Read a 64-bit signed integer in big-endian byte order.</summary>
    public long ReadInt64()
    {
        EnsureAvailable(sizeof(long));
        var value = BinaryPrimitives.ReadInt64BigEndian(buffer.Span.Slice(_position, sizeof(long)));
        _position += sizeof(long);
        return value;
    }

    /// <summary>
    /// Read a raw byte sequence of the given length. Returns a zero-copy slice
    /// of the underlying buffer; do not retain it past the buffer's lifetime.
    /// Mirrors cppcache <c>DataInput::readBytesOnly</c>.
    /// </summary>
    public ReadOnlyMemory<byte> ReadBytesOnly(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Length must be non-negative.");
        EnsureAvailable(count);
        var slice = buffer.Slice(_position, count);
        _position += count;
        return slice;
    }

    // ======================================================================
    //  Prototype — additional primitives, fill in when first needed
    // ======================================================================

    /// <summary>Read a signed 8-bit integer (i8).</summary>
    /// <remarks>
    /// Two's-complement reinterpretation of the next wire byte (e.g. <c>0xFF</c>
    /// → <c>-1</c>), matching what Java's <c>DataInput::readByte</c> returns.
    /// </remarks>
    public sbyte ReadSByte() => (sbyte)ReadByte();

    /// <summary>Read a 16-bit signed integer in big-endian byte order.</summary>
    public short ReadInt16()
    {
        EnsureAvailable(sizeof(short));
        var value = BinaryPrimitives.ReadInt16BigEndian(buffer.Span.Slice(_position, sizeof(short)));
        _position += sizeof(short);
        return value;
    }

    /// <summary>Read a 16-bit unsigned integer in big-endian byte order. Mirrors cppcache <c>readChar</c>.</summary>
    public ushort ReadUInt16()
    {
        EnsureAvailable(sizeof(ushort));
        var value = BinaryPrimitives.ReadUInt16BigEndian(buffer.Span.Slice(_position, sizeof(ushort)));
        _position += sizeof(ushort);
        return value;
    }

    /// <summary>Read a 32-bit unsigned integer in big-endian byte order.</summary>
    public uint ReadUInt32()
    {
        EnsureAvailable(sizeof(uint));
        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer.Span.Slice(_position, sizeof(uint)));
        _position += sizeof(uint);
        return value;
    }

    /// <summary>Read a 64-bit unsigned integer in big-endian byte order.</summary>
    public ulong ReadUInt64()
    {
        EnsureAvailable(sizeof(ulong));
        var value = BinaryPrimitives.ReadUInt64BigEndian(buffer.Span.Slice(_position, sizeof(ulong)));
        _position += sizeof(ulong);
        return value;
    }

    /// <summary>Read an IEEE 754 single-precision float in big-endian byte order.</summary>
    public float ReadFloat()
    {
        EnsureAvailable(sizeof(float));
        var value = BinaryPrimitives.ReadSingleBigEndian(buffer.Span.Slice(_position, sizeof(float)));
        _position += sizeof(float);
        return value;
    }

    /// <summary>Read an IEEE 754 double-precision float in big-endian byte order.</summary>
    public double ReadDouble()
    {
        EnsureAvailable(sizeof(double));
        var value = BinaryPrimitives.ReadDoubleBigEndian(buffer.Span.Slice(_position, sizeof(double)));
        _position += sizeof(double);
        return value;
    }

    /// <summary>
    /// Read a length-prefixed byte sequence: <see cref="ReadArrayLen"/>
    /// length (varint) followed by the bytes, or <c>null</c> if the
    /// sentinel is <c>-1</c>. Inverse of
    /// <see cref="BigEndianBinaryWriter.WriteBytes"/>; mirrors cppcache
    /// <c>DataInput::readBytes</c>.
    /// </summary>
    public byte[]? ReadBytes()
    {
        var length = ReadArrayLen();
        if (length == -1) return null;
        return ReadBytesOnly(length).ToArray();
    }

    /// <summary>
    /// Read Geode's variable-length array length encoding (1, 3, or 5
    /// bytes). Inverse of <see cref="BigEndianBinaryWriter.WriteArrayLen"/>;
    /// mirrors cppcache <c>DataInput::readArrayLen</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>First byte = <c>0xFF</c>            → returns <c>-1</c> (null sentinel).</item>
    ///   <item>First byte = <c>0xFE</c>            → next u16 BE is the length.</item>
    ///   <item>First byte = <c>0xFD</c>            → next i32 BE is the length.</item>
    ///   <item>First byte ≤ <c>252</c> (0xFC)      → that byte is the length.</item>
    /// </list>
    /// </remarks>
    public int ReadArrayLen()
    {
        var first = ReadSByte();
        return first switch
        {
            -1 => -1,            // 0xFF — null sentinel
            -2 => ReadUInt16(),  // 0xFE — u16 follows
            -3 => ReadInt32(),   // 0xFD — i32 follows
            _ => first,          // 0–252 — literal length
        };
    }

    /// <summary>
    /// Read a Java modified UTF-8 string with a u16 byte-length prefix.
    /// Mirrors cppcache <c>DataInput::readUTF</c>.
    /// </summary>
    /// <remarks>
    /// Modified UTF-8 differs from standard UTF-8: <c>0xC0 0x80</c> decodes
    /// to <c>\0</c>, and supplementary codepoints arrive as a surrogate pair
    /// of two 3-byte sequences (6 bytes total) rather than the 4-byte UTF-8
    /// form.
    /// </remarks>
    public string? ReadJavaModifiedUtf8() =>
        throw new NotImplementedException("Phase 4 string values.");

    /// <summary>
    /// Read a UTF-16 big-endian string with an i32 byte-length prefix.
    /// Mirrors cppcache <c>DataInput::readUtf16Huge</c>.
    /// </summary>
    public string? ReadUtf16Huge() =>
        throw new NotImplementedException("Phase 4 large string values.");

    // ======================================================================
    //  Internal helpers
    // ======================================================================

    private void EnsureAvailable(int needed)
    {
        if (_position + needed > buffer.Length)
        {
            throw new EndOfStreamException(
                $"Tried to read {needed} byte(s) at position {_position}, but only {buffer.Length - _position} remain.");
        }
    }
}
