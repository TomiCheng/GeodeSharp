using System.Buffers;
using System.Buffers.Binary;

namespace Geode.Client.Protocol;

/// <summary>
/// Sequential big-endian writer over an external <see cref="IBufferWriter{Byte}"/>.
/// C# counterpart of cppcache <c>DataOutput</c> / <c>java.io.DataOutput</c>:
/// every multi-byte primitive is written in network byte order so the bytes
/// match what a Geode server expects.
/// </summary>
/// <remarks>
/// <para>
/// Buffer ownership lives outside this class. Caller supplies any
/// <see cref="IBufferWriter{Byte}"/> — typically:
/// </para>
/// <list type="bullet">
///   <item><see cref="ArrayBufferWriter{Byte}"/> for in-memory encoding,</item>
///   <item><c>System.IO.Pipelines.PipeWriter</c> for direct-to-socket
///         writing in the Phase 6+ transport layer,</item>
///   <item>a custom pooled / capturing writer for tests or buffer reuse.</item>
/// </list>
/// <para>
/// The encoder is purely synchronous and write-only: flushing, lifetime,
/// and "give me the bytes" are the buffer owner's concerns.
/// </para>
/// <para>
/// Not thread-safe. BCL's <c>System.IO.BinaryWriter</c> is little-endian,
/// hence the explicit "BigEndian" prefix on this type — do not confuse the
/// two. Methods marked "prototype" throw <see cref="NotImplementedException"/>
/// and will be filled in as later phases need them.
/// </para>
/// </remarks>
internal sealed class BigEndianBinaryWriter(IBufferWriter<byte> output)
{
    private readonly IBufferWriter<byte> _output = output;
    private int _length;


    /// <summary>Bytes written so far through this writer.</summary>
    public int Length => _length;

    // ======================================================================
    //  Implemented (Phase 1 — frame codec)
    // ======================================================================

    /// <summary>Write a single unsigned byte (u8).</summary>
    public void WriteByte(byte value)
    {
        var span = _output.GetSpan(1);
        span[0] = value;
        _output.Advance(1);
        _length++;
    }

    /// <summary>Write a boolean as a single byte (1 = true, 0 = false).</summary>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Write a 32-bit signed integer in big-endian byte order.</summary>
    public void WriteInt32(int value)
    {
        var span = _output.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        _output.Advance(sizeof(int));
        _length += sizeof(int);
    }

    /// <summary>Write a 64-bit signed integer in big-endian byte order.</summary>
    public void WriteInt64(long value)
    {
        var span = _output.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        _output.Advance(sizeof(long));
        _length += sizeof(long);
    }

    /// <summary>
    /// Write a raw byte sequence verbatim (no length prefix, no transformation).
    /// Mirrors cppcache <c>DataOutput::writeBytesOnly</c>.
    /// </summary>
    public void WriteBytesOnly(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        var span = _output.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _output.Advance(bytes.Length);
        _length += bytes.Length;
    }

    // ======================================================================
    //  Prototype — additional primitives, fill in when first needed
    // ======================================================================

    /// <summary>Write a signed 8-bit integer (i8).</summary>
    /// <remarks>
    /// Two's-complement reinterpretation: <c>(byte)value</c> produces the same
    /// bit pattern that Java's <c>DataOutput::writeByte</c> writes for an
    /// <c>int8_t</c> (e.g. <c>-1</c> → <c>0xFF</c>).
    /// </remarks>
    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    /// <summary>Write a 16-bit signed integer in big-endian byte order.</summary>
    public void WriteInt16(short value)
    {
        var span = _output.GetSpan(sizeof(short));
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        _output.Advance(sizeof(short));
        _length += sizeof(short);
    }

    /// <summary>Write a 16-bit unsigned integer in big-endian byte order. Mirrors cppcache <c>writeChar</c>.</summary>
    public void WriteUInt16(ushort value)
    {
        var span = _output.GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        _output.Advance(sizeof(ushort));
        _length += sizeof(ushort);
    }

    /// <summary>Write a 32-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt32(uint value)
    {
        var span = _output.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        _output.Advance(sizeof(uint));
        _length += sizeof(uint);
    }

    /// <summary>Write a 64-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt64(ulong value)
    {
        var span = _output.GetSpan(sizeof(ulong));
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        _output.Advance(sizeof(ulong));
        _length += sizeof(ulong);
    }

    /// <summary>Write an IEEE 754 single-precision float in big-endian byte order.</summary>
    public void WriteFloat(float value)
    {
        var span = _output.GetSpan(sizeof(float));
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        _output.Advance(sizeof(float));
        _length += sizeof(float);
    }

    /// <summary>Write an IEEE 754 double-precision float in big-endian byte order.</summary>
    public void WriteDouble(double value)
    {
        var span = _output.GetSpan(sizeof(double));
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        _output.Advance(sizeof(double));
        _length += sizeof(double);
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
        WriteBytesOnly(bytes);
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

        // ASCII bulk write: ask the underlying writer for one span big
        // enough to hold the whole body, fill it, advance once.
        var body = _output.GetSpan(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            body[i] = (byte)value[i];
        }
        _output.Advance(value.Length);
        _length += value.Length;
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

        if (byteLen == 0) return;

        // Pass 2: emit the bytes as one bulk span write.
        var body = _output.GetSpan(byteLen);
        var pos = 0;
        foreach (var c in s)
        {
            if (c >= 0x0001 && c <= 0x007F)
            {
                body[pos++] = (byte)c;
            }
            else if (c == 0 || (c >= 0x0080 && c <= 0x07FF))
            {
                body[pos++] = (byte)(0xC0 | (c >> 6));
                body[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                body[pos++] = (byte)(0xE0 | (c >> 12));
                body[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                body[pos++] = (byte)(0x80 | (c & 0x3F));
            }
        }
        _output.Advance(byteLen);
        _length += byteLen;
    }

    /// <summary>
    /// Write a string as UTF-16 big-endian with an i32 byte-length prefix.
    /// Used for strings whose modified-UTF-8 length would exceed 65535 bytes.
    /// Mirrors cppcache <c>DataOutput::writeUtf16Huge</c>.
    /// </summary>
    public void WriteUtf16Huge(string? value) =>
        throw new NotImplementedException("Phase 4 large string values.");
}
