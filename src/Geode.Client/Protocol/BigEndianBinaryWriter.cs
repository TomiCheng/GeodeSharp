using System.Buffers.Binary;

namespace Geode.Client.Protocol;

/// <summary>
/// Sequential big-endian writer over an in-memory buffer.
/// C# counterpart of cppcache <c>DataOutput</c> / <c>java.io.DataOutput</c>:
/// every multi-byte primitive is written in network byte order so the bytes
/// match what a Geode server expects.
/// </summary>
/// <remarks>
/// Not thread-safe. Single producer, write-only. Call <see cref="ToArray"/>
/// once you are done to get the encoded payload.
///
/// BCL's <c>System.IO.BinaryWriter</c> is little-endian, hence the explicit
/// "BigEndian" prefix on this type — do not confuse the two.
///
/// Methods marked "prototype" throw <see cref="NotImplementedException"/>
/// and will be filled in as later phases need them.
/// </remarks>
internal sealed class BigEndianBinaryWriter
{
    private readonly MemoryStream _buffer = new();

    /// <summary>Bytes written so far.</summary>
    public int Length => (int)_buffer.Length;

    // ======================================================================
    //  Implemented (Phase 1 — frame codec)
    // ======================================================================

    /// <summary>Write a single unsigned byte (u8).</summary>
    public void WriteByte(byte value) => _buffer.WriteByte(value);

    /// <summary>Write a boolean as a single byte (1 = true, 0 = false).</summary>
    public void WriteBool(bool value) => _buffer.WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Write a 32-bit signed integer in big-endian byte order.</summary>
    public void WriteInt32(int value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write a 64-bit signed integer in big-endian byte order.</summary>
    public void WriteInt64(long value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>
    /// Write a raw byte sequence verbatim (no length prefix, no transformation).
    /// Mirrors cppcache <c>DataOutput::writeBytesOnly</c>.
    /// </summary>
    public void WriteBytesOnly(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

    /// <summary>Return a copy of all bytes written so far.</summary>
    public byte[] ToArray() => _buffer.ToArray();

    // ======================================================================
    //  Prototype — additional primitives, fill in when first needed
    // ======================================================================

    /// <summary>Write a signed 8-bit integer (i8).</summary>
    /// <remarks>
    /// Two's-complement reinterpretation: <c>(byte)value</c> produces the same
    /// bit pattern that Java's <c>DataOutput::writeByte</c> writes for an
    /// <c>int8_t</c> (e.g. <c>-1</c> → <c>0xFF</c>).
    /// </remarks>
    public void WriteSByte(sbyte value) => _buffer.WriteByte((byte)value);

    /// <summary>Write a 16-bit signed integer in big-endian byte order.</summary>
    public void WriteInt16(short value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write a 16-bit unsigned integer in big-endian byte order. Mirrors cppcache <c>writeChar</c>.</summary>
    public void WriteUInt16(ushort value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write a 32-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt32(uint value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write a 64-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt64(ulong value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write an IEEE 754 single-precision float in big-endian byte order.</summary>
    public void WriteFloat(float value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleBigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>Write an IEEE 754 double-precision float in big-endian byte order.</summary>
    public void WriteDouble(double value)
    {
        Span<byte> tmp = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleBigEndian(tmp, value);
        _buffer.Write(tmp);
    }

    /// <summary>
    /// Write a length-prefixed byte sequence: <see cref="WriteArrayLen"/>
    /// length (varint) followed by the bytes, or a single <c>-1</c> sentinel
    /// byte if <paramref name="bytes"/> is <c>null</c>.
    /// Mirrors cppcache <c>DataOutput::writeBytes</c>.
    /// </summary>
    public void WriteBytes(byte[]? bytes)
    {
        if (bytes is null)
        {
            WriteArrayLen(-1);
            return;
        }
        WriteArrayLen(bytes.Length);
        _buffer.Write(bytes);
    }

    /// <summary>
    /// Write Geode's variable-length array-length encoding (1, 3, or 5 bytes
    /// total). Mirrors cppcache <c>DataOutput::writeArrayLen</c>.
    /// </summary>
    /// <remarks>
    /// Encoding (matches Java collection-length convention):
    /// <list type="bullet">
    ///   <item><c>length == -1</c>          → 1 byte: <c>0xFF</c> (null sentinel).</item>
    ///   <item><c>length ≤ 252</c>          → 1 byte: the length itself.</item>
    ///   <item><c>length ≤ 0xFFFF</c>       → 3 bytes: <c>0xFE</c> + u16 length.</item>
    ///   <item>otherwise (up to int.MaxValue) → 5 bytes: <c>0xFD</c> + i32 length.</item>
    /// </list>
    /// </remarks>
    public void WriteArrayLen(int length)
    {
        if (length == -1)
        {
            WriteSByte(-1);
        }
        else if (length <= 252)
        {
            WriteByte((byte)length);
        }
        else if (length <= 0xFFFF)
        {
            WriteSByte(-2);
            WriteUInt16((ushort)length);
        }
        else
        {
            WriteSByte(-3);
            WriteInt32(length);
        }
    }

    /// <summary>
    /// Write a string in Java modified UTF-8 with a u16 byte-length prefix.
    /// Mirrors cppcache <c>DataOutput::writeUTF</c> / <c>writeJavaModifiedUtf8</c>.
    /// </summary>
    /// <remarks>
    /// Modified UTF-8 differs from standard UTF-8 in two places: <c>\0</c> is
    /// encoded as the two bytes <c>0xC0 0x80</c> (never a single zero byte),
    /// and characters above U+FFFF are encoded as a surrogate pair, each
    /// surrogate written as a 3-byte sequence (so a single supplementary
    /// codepoint takes 6 bytes, not 4 as in standard UTF-8).
    /// </remarks>
    /// <summary>
    /// Write a Geode-tagged string: <c>[DSCode byte][body]</c>. Mirrors
    /// cppcache <c>DataOutput::writeString</c>; the matching reader on the
    /// server is <c>StaticSerialization.readString</c>, which switches on
    /// the leading DSCode byte.
    /// </summary>
    /// <remarks>
    /// Branches:
    /// <list type="bullet">
    ///   <item><c>null</c> → 1 byte: <c>CacheableNullString (69)</c>.</item>
    ///   <item>
    ///     All ASCII (no NUL, all chars ≤ 0x7F), length ≤ 0xFFFF →
    ///     <c>CacheableASCIIString (87)</c> + <c>u16</c> length + ASCII bytes.
    ///   </item>
    ///   <item>
    ///     Has non-ASCII chars, modified-UTF-8 byte length ≤ 0xFFFF →
    ///     <c>CacheableString (42)</c> + <c>u16</c> byte-length + modified-UTF-8 bytes.
    ///   </item>
    ///   <item>
    ///     Lengths exceeding <c>0xFFFF</c> map to the <c>*Huge</c> DSCode
    ///     variants (88 / 89). Not implemented yet — throws; fill in when a
    ///     wire field with a huge string actually appears.
    ///   </item>
    /// </list>
    /// </remarks>
    public void WriteString(string? value)
    {
        const byte CacheableString = 42;
        const byte CacheableNullString = 69;
        const byte CacheableAsciiString = 87;

        if (value is null)
        {
            WriteByte(CacheableNullString);
            return;
        }

        var hasNonAscii = false;
        foreach (var c in value)
        {
            if (c == 0 || c > 0x007F)
            {
                hasNonAscii = true;
                break;
            }
        }

        if (hasNonAscii)
        {
            // CacheableString: leading byte + u16 byte-length + modified UTF-8.
            // WriteJavaModifiedUtf8 already emits the u16 prefix + body, so
            // we just stamp the DSCode in front and delegate.
            WriteByte(CacheableString);
            WriteJavaModifiedUtf8(value);
            return;
        }

        if (value.Length > 0xFFFF)
        {
            throw new NotImplementedException(
                $"CacheableASCIIStringHuge encoding (string length {value.Length} > 65535) " +
                "is not implemented; add when a real wire field needs it.");
        }

        WriteByte(CacheableAsciiString);
        WriteUInt16((ushort)value.Length);
        foreach (var c in value)
        {
            _buffer.WriteByte((byte)c);
        }
    }

    public void WriteJavaModifiedUtf8(string? value)
    {
        var s = value ?? string.Empty;

        // Pass 1: compute the modified-UTF-8 byte length so we can write the
        // u16 length prefix in one shot. We walk per UTF-16 code unit (char);
        // surrogate halves naturally fall into the 3-byte branch and a
        // supplementary code point ends up as 6 bytes — exactly what Java
        // modified UTF-8 calls for.
        int byteLen = 0;
        foreach (var c in s)
        {
            if (c >= 0x0001 && c <= 0x007F)
            {
                byteLen += 1;
            }
            else if (c == 0 || (c >= 0x0080 && c <= 0x07FF))
            {
                byteLen += 2;
            }
            else
            {
                byteLen += 3;
            }
        }

        if (byteLen > 0xFFFF)
        {
            throw new FormatException(
                $"String too long for Java modified UTF-8: {byteLen} bytes (max 65535).");
        }

        WriteUInt16((ushort)byteLen);

        // Pass 2: emit the bytes.
        Span<byte> buf = stackalloc byte[3];
        foreach (var c in s)
        {
            if (c >= 0x0001 && c <= 0x007F)
            {
                _buffer.WriteByte((byte)c);
            }
            else if (c == 0 || (c >= 0x0080 && c <= 0x07FF))
            {
                buf[0] = (byte)(0xC0 | (c >> 6));
                buf[1] = (byte)(0x80 | (c & 0x3F));
                _buffer.Write(buf[..2]);
            }
            else
            {
                buf[0] = (byte)(0xE0 | (c >> 12));
                buf[1] = (byte)(0x80 | ((c >> 6) & 0x3F));
                buf[2] = (byte)(0x80 | (c & 0x3F));
                _buffer.Write(buf);
            }
        }
    }

    /// <summary>
    /// Write a string as UTF-16 big-endian with an i32 byte-length prefix.
    /// Used for strings whose modified-UTF-8 length would exceed 65535 bytes.
    /// Mirrors cppcache <c>DataOutput::writeUtf16Huge</c>.
    /// </summary>
    public void WriteUtf16Huge(string? value) =>
        throw new NotImplementedException("Phase 4 large string values.");
}
