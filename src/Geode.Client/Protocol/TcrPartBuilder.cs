using Geode.Client.Protocol;

internal sealed class TcrPartBuilder(Func<CancellationToken, ValueTask<TcrPart>> func)
{
    public async ValueTask<TcrPart> BuildAsync(CancellationToken ct = default)
    {
        return await func.Invoke(ct);
    }

    public static TcrPartBuilder RawBytes(ReadOnlyMemory<byte> bytes)
        => new((_) => ValueTask.FromResult(new TcrPart(0, bytes)));


    public static TcrPartBuilder KeepAlive(bool value)
        => RawBytes(new byte[] { (byte)(value ? 1 : 0) });

}

/*
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

/// <summary>
/// Helpers for constructing <see cref="TcrPart"/> values without
/// repeating the buffer / writer plumbing at every call site.
/// </summary>
/// <remarks>
/// <para>
/// A Part is <c>i32 length</c> + <c>u8 IsObject</c> + payload bytes
/// (see <see cref="TcrPart"/>). Building one usually means: allocate a
/// buffer, wrap a <see cref="DataOutput"/>, write the
/// payload, then snapshot the bytes. The handful of methods here cover
/// the common shapes that show up across cppcache's <c>writeXxxPart</c>
/// helpers in <c>cppcache/src/TcrMessage.cpp</c>:
/// </para>
/// <list type="bullet">
///   <item><see cref="RawBytes"/> ??a verbatim byte buffer with
///         <c>IsObject=0</c> (region name, byte[] CacheableBytes shortcut).</item>
///   <item><see cref="Int32"/> ??single i32 BE payload, <c>IsObject=0</c>
///         (flags). Mirrors <c>writeIntPart</c>.</item>
///   <item><see cref="NullObj"/> ??single DSCode-41 byte (operation
///         placeholder, missing value sentinel).</item>
///   <item><see cref="CacheableBoolean"/> ??DSCode-53 + 1 byte
///         (isDelta, optional flags).</item>
///   <item><see cref="EmptyCacheableBytes"/> ??special <c>IsObject=2</c>
///         empty-payload sentinel for empty <c>byte[]</c> values.</item>
///   <item><see cref="Object"/> ??payload begins with a DSCode and the
///         caller writes the body via the writer.</item>
///   <item><see cref="Raw"/> ??like <see cref="Object"/> but
///         <c>IsObject=0</c> (EventId, raw blobs).</item>
/// </list>
/// </remarks>
internal sealed class TcrPartBuilder(IServiceProvider serviceProvider)
{

    /// <summary>
    /// Build a Part for a region path / OQL string &#x2014; thin
    /// semantic wrapper over <see cref="ModifiedUtf8"/>. cppcache's
    /// <c>writeRegionPart</c> writes raw <c>std::string</c> bytes
    /// without conversion; for typical ASCII region paths
    /// (<c>/orders</c>, <c>/users</c>) Modified UTF-8 produces the
    /// same bytes, so wire compatibility is preserved. For non-ASCII
    /// region names we get the encoding cppcache de-facto relies on
    /// (its callers ship UTF-8 <c>std::string</c>s) but does not
    /// guarantee.
    /// </summary>
    public TcrPart RegionName(string regionName) => ModifiedUtf8(regionName);

    /// <summary>
    /// Build a Part whose payload is <paramref name="value"/> encoded as
    /// <b>Java Modified UTF-8</b> (<c>IsObject=0</c>, no length prefix
    /// inside the payload &#x2014; the Part header alone carries the
    /// length).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors what the Geode Java server expects for region-name /
    /// OQL parts: it decodes via
    /// <c>CacheServerHelper.fromUTF(byte[])</c>
    /// (<c>geode-core/.../Part.java:174</c> +
    /// <c>CacheServerHelper.java:116</c>), the standard Java
    /// <c>DataInput.readUTF</c> decoder. Bytes hit the wire raw ??the
    /// u16 length prefix that <c>readUTF</c> would normally consume is
    /// absent because the surrounding Part header already supplies the
    /// length.
    /// </para>
    /// <para>
    /// cppcache's <c>writeRegionPart</c> does not encode at all &#x2014;
    /// it writes the bytes of the caller's <c>std::string</c> verbatim.
    /// That happens to match server-side modified-UTF-8 for the typical
    /// BMP / non-NUL characters real-world callers pass, but corrupts
    /// on NUL (one byte vs <c>0xC0 0x80</c>) and supplementary-plane
    /// code points (UTF-8 4-byte form vs modified UTF-8's 6-byte
    /// surrogate pair). This helper does the encoding explicitly so we
    /// stay correct in the corner cases cppcache silently mishandles.
    /// </para>
    /// <para>
    /// Encoding logic duplicates the body pass of
    /// <see cref="DataOutput.WriteJavaModifiedUtf8"/> (which
    /// also emits a u16 prefix we do not want for raw Parts). If a
    /// third caller materialises, extract a shared body writer.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    public TcrPart ModifiedUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Pass 1 ??pre-compute the byte length so the Part buffer is
        // sized exactly (no dynamic growth, no oversize allocation).
        var byteLen = 0;
        foreach (var c in value)
        {
            if (c >= 0x0001 && c <= 0x007F) byteLen += 1;
            else if (c == 0 || (c >= 0x0080 && c <= 0x07FF)) byteLen += 2;
            else byteLen += 3;
        }

        // Pass 2 ??emit the bytes through the standard Raw(IsObject=0)
        // path. Per-char branch matches Java DataOutput.writeUTF body
        // exactly (BMP only; supplementary chars arrive here as two
        // UTF-16 surrogate halves, each emitted as 3 bytes = 6 bytes
        // total ??same as Java).
        return Raw(w =>
        {
            foreach (var c in value)
            {
                if (c >= 0x0001 && c <= 0x007F)
                {
                    w.WriteByte((byte)c);
                }
                else if (c == 0 || (c >= 0x0080 && c <= 0x07FF))
                {
                    w.WriteByte((byte)(0xC0 | (c >> 6)));
                    w.WriteByte((byte)(0x80 | (c & 0x3F)));
                }
                else
                {
                    w.WriteByte((byte)(0xE0 | (c >> 12)));
                    w.WriteByte((byte)(0x80 | ((c >> 6) & 0x3F)));
                    w.WriteByte((byte)(0x80 | (c & 0x3F)));
                }
            }
        }, sizeHint: byteLen);
    }
    /// <summary>
    /// Wrap raw bytes as a Part with <c>IsObject=0</c>. No DSCode, no
    /// length prefix in the payload ??Part header alone supplies the
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
    /// Empty CacheableBytes sentinel ??<c>IsObject=2</c>, zero-length
    /// payload. Mirrors the empty branch of cppcache
    /// <c>writeObjectPart</c>'s CacheableBytes path.
    /// </summary>
    public TcrPart EmptyCacheableBytes() =>
        new(IsObject: 2, Payload: ReadOnlyMemory<byte>.Empty);

    /// <summary>
    /// Build a Part whose payload starts with a DSCode (<c>IsObject=1</c>).
    /// Caller writes the entire body ??including the leading DSCode byte
    /// ??via <paramref name="write"/>.
    /// </summary>
    /// <param name="write">Body writer; typically calls one of
    /// <see cref="DataOutput.WriteString"/>,
    /// <c>WriteByte(DSCode.X) + WriteInt32(...)</c>, etc.</param>
    /// <param name="sizeHint">Optional initial buffer size hint.</param>
    public TcrPart Object(Action<DataOutput> write, int sizeHint = 0) =>
        Build(isObject: 1, sizeHint, write);

    /// <summary>Async 版本的 <see cref="Object"/>;允許 body writer await(例如 PDX wire op)。</summary>
    public ValueTask<TcrPart> ObjectAsync(Func<DataOutput, ValueTask> write, int sizeHint = 0) =>
        BuildAsync(isObject: 1, sizeHint, write);

    /// <summary>
    /// Build a Part whose payload is a raw byte sequence (<c>IsObject=0</c>),
    /// composed by <paramref name="write"/>. Use for EventId and other
    /// non-DSCode-tagged compound payloads.
    /// </summary>
    /// <param name="write">Body writer.</param>
    /// <param name="sizeHint">Optional initial buffer size hint.</param>
    public TcrPart Raw(Action<DataOutput> write, int sizeHint = 0) =>
        Build(isObject: 0, sizeHint, write);

    private TcrPart Build(byte isObject, int sizeHint, Action<DataOutput> write)
    {
        // sizeHint hint is no longer plumbed (DataOutput starts at 8 KB
        // and grows). Re-add if a workload shows up needing tight control.
        _ = sizeHint;

        using var output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
        write(output);
        // Copy out — output's buffer returns to ArrayPool on Dispose.
        return new TcrPart(isObject, output.WrittenSpan.ToArray());
    }

    private async ValueTask<TcrPart> BuildAsync(byte isObject, int sizeHint, Func<DataOutput, ValueTask> write)
    {
        _ = sizeHint;

        using var output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
        await write(output);
        return new TcrPart(isObject, output.WrittenSpan.ToArray());
    }
}

*/
