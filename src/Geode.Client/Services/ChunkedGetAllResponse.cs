using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// <see cref="TcrChunkedResult"/> consumer for the chunked reply of a
/// <see cref="MessageType.GetAll70"/> request. Mirrors cppcache
/// <c>ChunkedGetAllResponse</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:487-549</c> +
/// <c>cppcache/src/ThinClientRegion.cpp:3616-3670</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.c status: empty skeleton.</b>
/// <see cref="HandleChunk"/> / <see cref="Reset"/> throw
/// <see cref="NotImplementedException"/>; <see cref="Values"/>
/// accessor returns the empty accumulator until those bodies land.
/// </para>
/// <para>
/// <b>Differs from <see cref="ChunkedPutAllResponse"/> /
/// <see cref="ChunkedRemoveAllResponse"/></b>: GetAll's reply ships
/// real values (server's <c>VersionedCacheableObjectPartList</c> sets
/// <c>hasObjects=true</c>), so this handler exercises
/// <see cref="VersionedCacheableObjectPartList"/>'s objects-section
/// decode path (step 5) &#x2014; the path 1.3.b wrote but never hit.
/// The accumulating <see cref="Values"/> dictionary is the actual
/// op return value, not a side-effect (PutAll / RemoveAll drop their
/// version-tag accumulators on the floor).
/// </para>
/// <para>
/// <b>Per-chunk wire shape</b> (cppcache <c>handleChunk</c>
/// classifies via <see cref="TcrMessageHelper.ReadChunkPartHeader"/>):
/// </para>
/// <list type="bullet">
///   <item><b>OBJECT</b> (<see cref="DSCode.FixedIDByte"/> +
///         <see cref="DSFid.VersionedObjectPartList"/>) &#x2014; one
///         <see cref="VersionedCacheableObjectPartList"/> instance
///         with <c>hasObjects=true</c>; decode N values back into the
///         shared <see cref="Values"/> dict indexed by
///         <c>Keys[index + keysOffset]</c>, then advance
///         <see cref="_keysOffset"/> by the consumed entry count.</item>
///   <item><b>EXCEPTION</b> &#x2014; per-chunk exception part;
///         <see cref="TcrMessageHelper.ReadChunkPartHeader"/> flips
///         the classifier and our caller's reply switch will throw.
///         Unlike PutAll / RemoveAll, GetAll has no NULL_OBJECT or
///         BYTES branch &#x2014; only Object or Exception
///         (cppcache <c>ThinClientRegion.cpp:3630-3637</c>).</item>
/// </list>
/// <para>
/// <b>Result accumulators</b> mirror cppcache's
/// <c>ChunkedGetAllResponse</c> 8-arg ctor
/// (<c>cppcache/src/ThinClientRegion.cpp:1122-1124</c>):
/// <c>m_keys</c> (caller-supplied, positional index source) /
/// <c>m_values</c> / <c>m_exceptions</c> / <c>m_resultKeys</c> /
/// <c>m_keysOffset</c> are accumulated across all chunks, then read
/// back by the caller after the dispatcher returns. Phase 1.3
/// exposes <see cref="Values"/> only &#x2014; per-key exceptions /
/// partial-result keys lands when the public surface grows a partial
/// API.
/// </para>
/// </remarks>
/// <param name="serviceProvider">DI scope for reader / VCOPL construction.</param>
/// <param name="logger">Severity-aligned with cppcache LOG* calls.</param>
/// <param name="tcrMessageHelper">Shared chunk-part-header decoder.</param>
/// <param name="region">Region this op runs against. Mirrors cppcache
/// <c>ChunkedGetAllResponse::m_region</c>.</param>
/// <param name="keys">Original keys we sent &#x2014; the chunked
/// reply's per-entry slot <c>i</c> maps back to <c>keys[i + keysOffset]</c>.
/// Mirrors cppcache <c>ChunkedGetAllResponse::m_keys</c>
/// (<c>const std::vector&lt;CacheableKey&gt;*</c>); we hold an
/// <see cref="IReadOnlyList{T}"/> for positional access.</param>
/// <param name="addToLocalCache">Whether <see cref="VersionedCacheableObjectPartList.FromData"/>
/// should run its step-7 <c>putLocal</c> merge (write decoded values
/// into the region's client-side cache). Mirrors cppcache
/// <c>ChunkedGetAllResponse::m_addToLocalCache</c>; the caller
/// (<see cref="ThinClientRegion.GetAllAsync"/>) ANDs the requested
/// flag with the region's <c>caching-enabled</c> attribute the same
/// way cppcache's <c>getAllNoThrow_remote</c> does
/// (<c>ThinClientRegion.cpp:1100</c>), so a proxy-mode region
/// (caching-enabled=false) sees this collapse to <c>false</c>
/// regardless of caller intent.</param>
/// <param name="msg">Reply <see cref="TcrMessage"/> for auth-trailer /
/// pool back-refs. Phase 3+ (auth) / Phase 4+ (single-hop) actually
/// read it; Phase 1.3 leaves <c>null</c>.</param>
internal sealed class ChunkedGetAllResponse(
    IServiceProvider serviceProvider,
    ILogger<ChunkedGetAllResponse> logger,
    TcrMessageHelper tcrMessageHelper,
    ThinClientRegion region,
    IReadOnlyList<object> keys,
    bool addToLocalCache,
    TcrMessage? msg = null) : TcrChunkedResult
{
    /// <summary>
    /// Per-key value accumulator filled by <see cref="HandleChunk"/>
    /// across all chunks. Mirrors cppcache
    /// <c>ChunkedGetAllResponse::m_values</c>
    /// (<c>std::shared_ptr&lt;HashMapOfCacheable&gt;</c>); element value
    /// nullable because GetAll's per-entry miss flag <c>3</c> stores
    /// <c>null</c> for keys the server doesn't have. Caller
    /// (<see cref="ThinClientRegion.GetAllAsync"/>) reads this after
    /// dispatch returns; exposed as <see cref="IReadOnlyDictionary{TKey, TValue}"/>
    /// so the public surface can't mutate it post-return.
    /// </summary>
    private readonly Dictionary<object, object?> _values = [];

    /// <summary>
    /// Per-key exception accumulator. Lazy &#x2014; only allocated
    /// when <see cref="VersionedCacheableObjectPartList.ReadObjectPart"/>
    /// hits the <c>objType==2</c> branch. Phase 1.3 doesn't surface
    /// per-key exceptions on the public API; the field stays for
    /// cppcache parity and to drain the wire correctly.
    /// </summary>
    private readonly Dictionary<object, GeodeException?>? _exceptions = null;

    /// <summary>
    /// Subset of <see cref="_values"/> keys the server actually
    /// returned data for. Mirrors cppcache
    /// <c>ChunkedGetAllResponse::m_resultKeys</c>; used by single-hop
    /// (Phase 4+) to skip refreshing metadata for keys the server
    /// already served. Phase 1.3 leaves <c>null</c>.
    /// </summary>
    private readonly List<object>? _resultKeys = null;

    /// <summary>
    /// Running cursor into <see cref="keys"/>. Advances by
    /// <see cref="VersionedCacheableObjectPartList.ConsumedObjectCount"/>
    /// after each chunk's <c>FromData</c> finishes. Mirrors cppcache
    /// <c>ChunkedGetAllResponse::m_keysOffset</c>
    /// (<c>uint32_t</c>); the pointer-vs-value distinction (cppcache
    /// uses <c>&amp;m_keysOffset</c> so VCOPL can read+write the same
    /// cursor) collapses to an explicit post-FromData read-back on
    /// the .NET side &#x2014; see <see cref="HandleChunk"/>.
    /// </summary>
    // Explicit init (0) silences CS0649 while HandleChunk's body is
    // still NIE; the real assignment site is in HandleChunk's
    // VCOPL-decode path (lands next).
    private int _keysOffset = 0;

    /// <summary>
    /// Per-key result. Keys are the same <see cref="object"/> instances
    /// the caller passed to <see cref="ThinClientRegion.GetAllAsync"/>
    /// (we never re-decode them off the wire &#x2014; the server doesn't
    /// echo keys back for GetAll, only values indexed positionally).
    /// Missing-on-server keys appear here with value <c>null</c>
    /// (cppcache <c>m_byteArray[i]==3</c> stores null). Read by
    /// <see cref="ThinClientRegion.GetAllAsync"/> after dispatch
    /// returns &#x2014; the result is then returned through the
    /// public API as <c>IReadOnlyDictionary&lt;object, object?&gt;</c>.
    /// </summary>
    public IReadOnlyDictionary<object, object?> Values => _values;

    public override void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk)
    {
        // Mirrors cppcache ChunkedGetAllResponse::handleChunk
        // (cppcache/src/ThinClientRegion.cpp:3623-3648). Shorter than
        // ChunkedPutAllResponse / ChunkedRemoveAllResponse because
        // GetAll's reply is strictly Object-or-Exception — no
        // NULL_OBJECT (server always ships values for a non-empty key
        // list) and no BYTES branch (cppcache doesn't enqueue PR
        // single-hop metadata refresh off GetAll replies).

        // ─── Step 1: wrap chunk bytes ──────────────────────────
        // cppcache: cacheImpl->createDataInput(chunk, chunkLen, pool).
        var reader = ActivatorUtilities.CreateInstance<BigEndianBinaryReader>(serviceProvider, payload);

        // ─── Step 2: read chunk part header ────────────────────
        // Peels partLen + isObj + DSCode/FixedID combo. Expected
        // leading DSCode = FixedIDByte (1-byte fixed-id follows);
        // expected partType = DSFid.VersionedObjectPartList. If the
        // classifier returns anything other than Object — typically
        // Exception — we drain + bail; the reply.MessageType flip
        // (handled inside ReadChunkPartHeader) routes the caller into
        // ThinClientRegion.GetAllAsync's EXCEPTION switch.
        var chunkType = tcrMessageHelper.ReadChunkPartHeader(
            reader,
            DSCode.FixedIDByte,
            (int)DSFid.VersionedObjectPartList,
            nameof(ChunkedGetAllResponse),
            out var partLen,
            isLastChunk: (byte)(isLastChunk ? 1 : 0));

        if (chunkType != TcrMessageHelper.ChunkObjectType.Object)
        {
            // cppcache: "encountered an exception part, so return
            // without reading more" (ThinClientRegion.cpp:3634-3637).
            // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
            // true, isLastChunkWithSecurity). Phase 1.3 no auth →
            // security bit always 0 → no trailer bytes to consume.
            logger.LogDebug(
                "ChunkedGetAllResponse::handleChunk non-OBJECT chunk (chunkType={ChunkType}) — bailing",
                chunkType);
            return;
        }

        // ─── Step 3: decode one VersionedCacheableObjectPartList ──
        // cppcache constructs VCOPL with the 11-arg ctor passing the
        // shared accumulators by pointer/ref so each chunk's fromData
        // writes into the same dicts. Our Initialize method is the
        // equivalent — keys are the caller-supplied list, keysOffset
        // is the running cursor, values / exceptions / resultKeys are
        // shared accumulators. addToLocalCache stays false for Phase
        // 1.3 (no client-side caching), which gates VCOPL.FromData's
        // step 7 (putLocal merge) into a no-op.
        var vcObjPart = ActivatorUtilities.CreateInstance<VersionedCacheableObjectPartList>(
            serviceProvider, region);
        vcObjPart.Initialize(
            keys: keys,
            keysOffset: _keysOffset,
            values: GetMutableValues(),
            exceptions: _exceptions,
            resultKeys: _resultKeys,
            addToLocalCache: addToLocalCache);
        vcObjPart.FromData(reader);

        // ─── Step 4: advance shared cursor ─────────────────────
        // cppcache passes &m_keysOffset so the per-chunk VCOPL writes
        // through; .NET doesn't have pointer-to-int semantics here, so
        // VCOPL exposes ConsumedObjectCount for the post-decode
        // read-back. The next chunk's VCOPL.Initialize will pick up
        // from the new cursor.
        _keysOffset += vcObjPart.ConsumedObjectCount;

        // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
        // true, isLastChunkWithSecurity).
        _ = partLen;
        _ = msg;
    }

    /// <summary>
    /// <see cref="_values"/> typed as the mutable
    /// <see cref="Dictionary{TKey, TValue}"/> for the
    /// <see cref="VersionedCacheableObjectPartList.Initialize"/> call.
    /// The public <see cref="Values"/> accessor narrows the surface to
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> so the caller
    /// can't mutate post-return; VCOPL.Initialize needs the mutable
    /// type to fill in entries during chunk decode.
    /// </summary>
    private Dictionary<object, object?> GetMutableValues() => _values;

    public override void Reset()
    {
        // Mirrors cppcache ChunkedGetAllResponse::reset
        // (cppcache/src/ThinClientRegion.cpp:3616-3621):
        //   void ChunkedGetAllResponse::reset() {
        //     m_keysOffset = 0;
        //     if (m_resultKeys != nullptr && m_resultKeys->size() > 0) {
        //       m_resultKeys->clear();
        //     }
        //   }

        // ─── Step 1: rewind cursor ────────────────────────────
        // Retry replays the chunks from scratch; the per-chunk decode
        // path advances _keysOffset by ConsumedObjectCount, so it has
        // to start at 0 again.
        _keysOffset = 0;

        // ─── Step 2: drop result-keys subset ──────────────────
        // cppcache null-guards via shared_ptr empty check; .NET via
        // ?.Count + ?.Clear. Phase 1.3 leaves _resultKeys null so
        // the conditional short-circuits — the field exists for
        // cppcache parity and Phase 4+ single-hop will start
        // populating it.
        if (_resultKeys is { Count: > 0 })
        {
            _resultKeys.Clear();
        }

        // Does NOT clear _values / _exceptions — cppcache leaves those
        // alone too; the retry's chunks overwrite slot-by-slot using
        // the same keys list, so stale entries from the failed attempt
        // get replaced rather than removed.
    }
}
