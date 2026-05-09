using System.Buffers;

namespace Geode.Client.Protocol;

/// <summary>
/// Helpers for constructing <see cref="TcrPart"/> values without
/// repeating the buffer / writer plumbing at every call site.
/// </summary>
/// <remarks>
/// <para>
/// A Part is <c>i32 length</c> + <c>u8 IsObject</c> + payload bytes
/// (see <see cref="TcrPart"/>). Building one usually means: allocate a
/// buffer, wrap a <see cref="BigEndianBinaryWriter"/>, write the
/// payload, then snapshot the bytes. The handful of methods here cover
/// the common shapes that show up across cppcache's <c>writeXxxPart</c>
/// helpers in <c>cppcache/src/TcrMessage.cpp</c>:
/// </para>
/// <list type="bullet">
///   <item><see cref="RawBytes"/> — a verbatim byte buffer with
///         <c>IsObject=0</c> (region name, byte[] CacheableBytes shortcut).</item>
///   <item><see cref="Int32"/> — single i32 BE payload, <c>IsObject=0</c>
///         (flags). Mirrors <c>writeIntPart</c>.</item>
///   <item><see cref="NullObj"/> — single DSCode-41 byte (operation
///         placeholder, missing value sentinel).</item>
///   <item><see cref="CacheableBoolean"/> — DSCode-53 + 1 byte
///         (isDelta, optional flags).</item>
///   <item><see cref="EmptyCacheableBytes"/> — special <c>IsObject=2</c>
///         empty-payload sentinel for empty <c>byte[]</c> values.</item>
///   <item><see cref="Object"/> — payload begins with a DSCode and the
///         caller writes the body via the writer.</item>
///   <item><see cref="Raw"/> — like <see cref="Object"/> but
///         <c>IsObject=0</c> (EventId, raw blobs).</item>
/// </list>
/// </remarks>
internal sealed class TcrPartBuilder
{
    /// <summary>
    /// Wrap raw bytes as a Part with <c>IsObject=0</c>. No DSCode, no
    /// length prefix in the payload — Part header alone supplies the
    /// length. Mirrors cppcache <c>writeRegionPart</c> and the
    /// CacheableBytes branch of <c>writeObjectPart</c>.
    /// </summary>
    public TcrPart RawBytes(ReadOnlyMemory<byte> bytes) =>
        new(IsObject: 0, Payload: bytes);

    /// <summary>
    /// Single i32 BE payload, <c>IsObject=0</c>. Mirrors cppcache
    /// <c>writeIntPart</c>.
    /// </summary>
    public TcrPart Int32(int value) =>
        Raw(w => w.WriteInt32(value), sizeHint: sizeof(int));

    /// <summary>
    /// One-byte payload of <see cref="DSCode.NullObj"/>, <c>IsObject=1</c>.
    /// Used for operation slots and missing value markers.
    /// </summary>
    public TcrPart NullObj() =>
        new(IsObject: 1, Payload: new byte[] { DSCode.NullObj });

    /// <summary>
    /// CacheableBoolean (<see cref="DSCode.CacheableBoolean"/> + 1 byte),
    /// <c>IsObject=1</c>.
    /// </summary>
    public TcrPart CacheableBoolean(bool value) =>
        new(
            IsObject: 1,
            Payload: new byte[] { DSCode.CacheableBoolean, value ? (byte)1 : (byte)0 });

    /// <summary>
    /// Empty CacheableBytes sentinel — <c>IsObject=2</c>, zero-length
    /// payload. Mirrors the empty branch of cppcache
    /// <c>writeObjectPart</c>'s CacheableBytes path.
    /// </summary>
    public TcrPart EmptyCacheableBytes() =>
        new(IsObject: 2, Payload: ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// Build a Part whose payload starts with a DSCode (<c>IsObject=1</c>).
    /// Caller writes the entire body — including the leading DSCode byte
    /// — via <paramref name="write"/>.
    /// </summary>
    /// <param name="write">Body writer; typically calls one of
    /// <see cref="BigEndianBinaryWriter.WriteString"/>,
    /// <c>WriteByte(DSCode.X) + WriteInt32(...)</c>, etc.</param>
    /// <param name="sizeHint">Optional initial buffer size hint.</param>
    public TcrPart Object(Action<BigEndianBinaryWriter> write, int sizeHint = 0) =>
        Build(isObject: 1, sizeHint, write);

    /// <summary>
    /// Build a Part whose payload is a raw byte sequence (<c>IsObject=0</c>),
    /// composed by <paramref name="write"/>. Use for EventId and other
    /// non-DSCode-tagged compound payloads.
    /// </summary>
    /// <param name="write">Body writer.</param>
    /// <param name="sizeHint">Optional initial buffer size hint.</param>
    public TcrPart Raw(Action<BigEndianBinaryWriter> write, int sizeHint = 0) =>
        Build(isObject: 0, sizeHint, write);

    private TcrPart Build(byte isObject, int sizeHint, Action<BigEndianBinaryWriter> write)
    {
        var buffer = sizeHint > 0
            ? new ArrayBufferWriter<byte>(sizeHint)
            : new ArrayBufferWriter<byte>();
        write(new BigEndianBinaryWriter(buffer));
        return new TcrPart(isObject, buffer.WrittenMemory);
    }
}
