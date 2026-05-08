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
    public void WriteSByte(sbyte value) =>
        throw new NotImplementedException("Phase 2 handshake.");

    /// <summary>Write a 16-bit signed integer in big-endian byte order.</summary>
    public void WriteInt16(short value) =>
        throw new NotImplementedException("Phase 2 handshake / Phase 4 typed values.");

    /// <summary>Write a 16-bit unsigned integer in big-endian byte order. Mirrors cppcache <c>writeChar</c>.</summary>
    public void WriteUInt16(ushort value) =>
        throw new NotImplementedException("Phase 4 typed values.");

    /// <summary>Write a 32-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt32(uint value) =>
        throw new NotImplementedException("Phase 4 typed values.");

    /// <summary>Write a 64-bit unsigned integer in big-endian byte order.</summary>
    public void WriteUInt64(ulong value) =>
        throw new NotImplementedException("Phase 4 typed values.");

    /// <summary>Write an IEEE 754 single-precision float in big-endian byte order.</summary>
    public void WriteFloat(float value) =>
        throw new NotImplementedException("Phase 4 typed values.");

    /// <summary>Write an IEEE 754 double-precision float in big-endian byte order.</summary>
    public void WriteDouble(double value) =>
        throw new NotImplementedException("Phase 4 typed values.");

    /// <summary>
    /// Write a length-prefixed byte sequence: i32 length followed by the bytes,
    /// or i32 -1 if <paramref name="bytes"/> is <c>null</c>.
    /// Mirrors cppcache <c>DataOutput::writeBytes</c>.
    /// </summary>
    public void WriteBytes(byte[]? bytes) =>
        throw new NotImplementedException("Phase 3 Put/Get value parts.");

    /// <summary>
    /// Write Geode's variable-length array length encoding (1, 2, or 4 bytes
    /// depending on magnitude). Mirrors cppcache <c>DataOutput::writeArrayLen</c>.
    /// </summary>
    public void WriteArrayLen(int length) =>
        throw new NotImplementedException("Phase 4 collection-bearing parts.");

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
    public void WriteJavaModifiedUtf8(string? value) =>
        throw new NotImplementedException("Phase 4 string values.");

    /// <summary>
    /// Write a string as UTF-16 big-endian with an i32 byte-length prefix.
    /// Used for strings whose modified-UTF-8 length would exceed 65535 bytes.
    /// Mirrors cppcache <c>DataOutput::writeUtf16Huge</c>.
    /// </summary>
    public void WriteUtf16Huge(string? value) =>
        throw new NotImplementedException("Phase 4 large string values.");
}
