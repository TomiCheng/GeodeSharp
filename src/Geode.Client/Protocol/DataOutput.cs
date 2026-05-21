using System.Buffers;
using System.Buffers.Binary;
using Geode.Client.Internal;
using Geode.Client.Protocol.Serialization;

namespace Geode.Client.Protocol;

/// <summary>
/// Owned, seekable big-endian write buffer. Mirror of cppcache
/// <c>DataOutput</c> (<c>cppcache/include/geode/DataOutput.hpp</c>).
/// Replaces <c>DataOutput</c>'s forward-only model
/// with a single mutable byte buffer + cursor, enabling header
/// back-fill (TcrMessage length, PdxLocalWriter header, etc.) in
/// place instead of via local-buffer-per-unit indirection.
/// </summary>
/// <remarks>
/// <para>
/// Buffer rented from <see cref="ArrayPool{T}.Shared"/>; returned on
/// <see cref="Dispose"/>. Matches cppcache <c>TSSDataOutput</c>
/// thread-local pool semantics (default rent size 8192 bytes).
/// </para>
/// <para>
/// Holds <see cref="SerializationRegistry"/> (and optionally an
/// <see cref="IPool"/>) for nested encode dispatch. cppcache reaches
/// these through <c>m_cache->getSerializationRegistry()</c> /
/// <c>m_pool</c>; we inject them directly because both are already
/// scoped DI services. Cache itself isn't needed here.
/// </para>
/// <para>
/// Construct via
/// <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/>,
/// not raw <c>new</c>, so <see cref="SerializationRegistry"/> resolves
/// from the scoped container:
/// <code>
/// using var output = ActivatorUtilities.CreateInstance&lt;DataOutput&gt;(sp);
/// using var output = ActivatorUtilities.CreateInstance&lt;DataOutput&gt;(sp, pool);
/// </code>
/// </para>
/// </remarks>
internal sealed class DataOutput(SerializationRegistry registry, IPool? pool = null)
    : IDisposable, IBufferWriter<byte>
{
    // cppcache TSSDataOutput::getBuffer default = 8192. Keep identical
    // so the typical message rent doesn't grow.
    private const int InitialSize = 8192;

    private byte[] _bytes = ArrayPool<byte>.Shared.Rent(InitialSize);
    private int _disposed;
    private int _position;
    private int _writtenCount;

    /// <summary>
    /// 拿 DataOutput 對應的 <see cref="SerializationRegistry"/>;PDX deserialize
    /// 流程未來需要它(read 端 schema 查 / register)。目前是讓 ctor 參數
    /// 不被當 unused 抱怨的存在,實際讀取方還沒接上。
    /// </summary>
    internal SerializationRegistry Registry => registry;

    private void EnsureCapacity(int additionalBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        var required = _position + additionalBytes;
        if (required <= _bytes.Length) return;

        // Double until enough, mirroring cppcache ensureCapacity growth
        // (it doubles too). ArrayPool gives at least requested; usually more.
        var newSize = _bytes.Length;
        while (newSize < required) newSize *= 2;
        var newBytes = ArrayPool<byte>.Shared.Rent(newSize);
        Array.Copy(_bytes, newBytes, _writtenCount);
        ArrayPool<byte>.Shared.Return(_bytes);
        _bytes = newBytes;
    }

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_position + count > _bytes.Length)
        {
            throw new InvalidOperationException(
                $"Advance({count}) past buffer end (pos={_position}, size={_bytes.Length}).");
        }
        _position += count;
        if (_position > _writtenCount) _writtenCount = _position;
    }

    /// <summary>
    /// Advance the cursor by <paramref name="n"/> bytes, growing the
    /// buffer if needed. Mirrors cppcache <c>DataOutput::advanceCursor</c>.
    /// </summary>
    public void AdvanceCursor(int n)
    {
        EnsureCapacity(n);
        _position += n;
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        ArrayPool<byte>.Shared.Return(_bytes);
        _bytes = [];
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(sizeHint > 0 ? sizeHint : 1);
        return _bytes.AsMemory(_position);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(sizeHint > 0 ? sizeHint : 1);
        return _bytes.AsSpan(_position);
    }

    /// <summary>
    /// Patch a 4-byte BE int at <paramref name="position"/> without
    /// moving the current cursor. Convenience for placeholder back-fill
    /// (mirrors cppcache <c>updateValueAtPos</c> for the 4-byte case).
    /// </summary>
    public void PatchInt32(int position, int value)
    {
        if (position < 0 || position + sizeof(int) > _writtenCount)
        {
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Patch range [{position}..{position + sizeof(int)}] " +
                $"exceeds written count {_writtenCount}.");
        }
        BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(position), value);
    }

    /// <summary>
    /// Rewind the cursor by <paramref name="n"/> bytes (does not shrink
    /// the high-water mark). Mirrors cppcache <c>DataOutput::rewindCursor</c>.
    /// </summary>
    public void RewindCursor(int n)
    {
        if (n < 0 || n > _position)
        {
            throw new ArgumentOutOfRangeException(nameof(n),
                $"Cannot rewind {n} from position {_position}.");
        }
        _position -= n;
    }

    /// <summary>Copy of the written bytes ??caller-owned.</summary>
    public byte[] ToArray() => _bytes.AsSpan(0, _writtenCount).ToArray();

    /// <summary>
    /// Geode array-length encoding (1 / 3 / 5 bytes). Mirrors cppcache
    /// <c>DataOutput::writeArrayLen</c>.
    /// </summary>
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
        else if (length <= 0xFFFF) { WriteSByte(-2); WriteUInt16((ushort)length); }
        else { WriteSByte(-3); WriteInt32(length); }
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _bytes[_position++] = value;
        if (_position > _writtenCount) _writtenCount = _position;
    }

    /// <summary>
    /// Length-prefixed byte sequence (<see cref="WriteArrayLen"/> +
    /// payload, or null sentinel). Mirrors cppcache <c>writeBytes</c>.
    /// </summary>
    public void WriteBytes(byte[]? bytes)
    {
        if (bytes is null) { WriteArrayLen(-1); return; }
        WriteArrayLen(bytes.Length);
        WriteBytesOnly(bytes);
    }

    /// <summary>
    /// Write a raw byte sequence verbatim. Mirrors cppcache <c>DataOutput::writeBytesOnly</c>.
    /// </summary>
    public void WriteBytesOnly(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        EnsureCapacity(bytes.Length);
        bytes.CopyTo(_bytes.AsSpan(_position));
        _position += bytes.Length;
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteDouble(double value)
    {
        EnsureCapacity(sizeof(double));
        BinaryPrimitives.WriteDoubleBigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(double);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteFloat(float value)
    {
        EnsureCapacity(sizeof(float));
        BinaryPrimitives.WriteSingleBigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(float);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteInt16(short value)
    {
        EnsureCapacity(sizeof(short));
        BinaryPrimitives.WriteInt16BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(short);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteInt32(int value)
    {
        EnsureCapacity(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(int);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteInt64(long value)
    {
        EnsureCapacity(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(long);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    /// <summary>Java modified UTF-8 with u16 byte-length prefix. Mirrors cppcache <c>writeJavaModifiedUtf8</c>.</summary>
    public void WriteJavaModifiedUtf8(string? value)
    {
        var s = value ?? string.Empty;

        var byteLen = 0;
        foreach (var c in s)
        {
            if (c >= 0x0001 && c <= 0x007F) byteLen += 1;
            else if (c == 0 || (c >= 0x0080 && c <= 0x07FF)) byteLen += 2;
            else byteLen += 3;
        }

        if (byteLen > 0xFFFF)
        {
            throw new FormatException(
                $"String too long for Java modified UTF-8: {byteLen} bytes (max 65535).");
        }

        WriteUInt16((ushort)byteLen);
        if (byteLen == 0) return;

        EnsureCapacity(byteLen);
        var body = _bytes.AsSpan(_position, byteLen);
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
        _position += byteLen;
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    /// <summary>
    /// DSCode-tagged string. Mirrors cppcache <c>DataOutput::writeString</c>.
    /// Null ??<c>CacheableNullString</c>; ASCII (??0xFFFF chars) ??    /// <c>CacheableASCIIString</c>; non-ASCII (mod-UTF-8 ??0xFFFF bytes) ??    /// <c>CacheableString</c>. Huge variants (88/89) NIE for now.
    /// </summary>
    public void WriteString(string? value)
    {
        if (value is null) { WriteByte(DSCode.CacheableNullString); return; }

        var hasNonAscii = false;
        foreach (var c in value)
        {
            if (c == 0 || c > 0x007F) { hasNonAscii = true; break; }
        }

        if (hasNonAscii)
        {
            WriteByte(DSCode.CacheableString);
            WriteJavaModifiedUtf8(value);
            return;
        }

        if (value.Length > 0xFFFF)
        {
            throw new NotImplementedException(
                $"CacheableASCIIStringHuge encoding (length {value.Length} > 65535) " +
                "is not implemented; add when a real wire field needs it.");
        }

        WriteByte(DSCode.CacheableASCIIString);
        WriteUInt16((ushort)value.Length);
        EnsureCapacity(value.Length);
        for (var i = 0; i < value.Length; i++) _bytes[_position++] = (byte)value[i];
        if (_position > _writtenCount) _writtenCount = _position;
    }

    /// <summary>Mirrors cppcache <c>writeChar</c> (Java <c>char</c> = u16).</summary>
    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(ushort);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(sizeof(uint));
        BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(uint);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    public void WriteUInt64(ulong value)
    {
        EnsureCapacity(sizeof(ulong));
        BinaryPrimitives.WriteUInt64BigEndian(_bytes.AsSpan(_position), value);
        _position += sizeof(ulong);
        if (_position > _writtenCount) _writtenCount = _position;
    }

    /// <summary>
    /// Target pool for this buffer's eventual wire send;
    /// <see langword="null"/> when not bound to a specific pool
    /// (e.g. before <c>EnsureInitializedAsync</c> registers a default).
    /// Mirrors cppcache <c>DataOutput::getPool()</c>; used by PDX
    /// type-id resolution to send <c>GetPdxIdForType</c> via the
    /// correct cluster (typeIds are per-cluster).
    /// </summary>
    public IPool? Pool => pool;

    /// <summary>
    /// Current cursor position. Setter is restricted to the existing
    /// written range ??use <see cref="AdvanceCursor"/> to grow.
    /// </summary>
    public int Position
    {
        get => _position;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (value < 0 || value > _writtenCount)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Position {value} out of range [0, {_writtenCount}].");
            }
            _position = value;
        }
    }

    /// <summary>Total bytes written so far (high-water mark, not current cursor).</summary>
    public int WrittenCount => _writtenCount;

    /// <summary>Bytes written so far (up to the high-water mark).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _bytes.AsSpan(0, _writtenCount);

}
