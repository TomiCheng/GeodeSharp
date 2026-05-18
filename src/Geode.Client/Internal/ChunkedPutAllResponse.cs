using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="TcrChunkedResult"/> consumer for the chunked reply of a
/// <see cref="MessageType.PutAll"/> request. Mirrors cppcache
/// <c>ChunkedPutAllResponse</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:554-583</c> +
/// <c>cppcache/src/ThinClientRegion.cpp:3672-3727</c>).
/// </summary>
/// <remarks>
/// <para>
/// Wire shape mirrors <see cref="ChunkedRemoveAllResponse"/> 1:1
/// &#x2014; both PutAll and RemoveAll replies ship only per-key
/// version tags (no keys echoed, no values echoed), so the handler
/// is structurally a copy with only the diagnostic log strings
/// swapped. Phase 1.3 caller leaves the <c>list</c> accumulator
/// <c>null</c> &#x2014; per-key version tags are discarded; the
/// handler still drains the chunk bytes so the reader stays aligned.
/// </para>
/// <para>
/// <b>Expected payload per chunk</b> (same three shapes as
/// <see cref="ChunkedRemoveAllResponse"/>, classified via
/// <see cref="TcrMessageHelper.ReadChunkPartHeader"/>):
/// </para>
/// <list type="bullet">
///   <item><b>NULL_OBJECT</b> &#x2014; server has no version info to
///         ship (empty batch / caching disabled). Consume the
///         secure-object trailer and return.</item>
///   <item><b>OBJECT</b> (<see cref="DSCode.FixedIDByte"/> +
///         <see cref="DSFid.VersionedObjectPartList"/>) &#x2014; one
///         <see cref="VersionedCacheableObjectPartList"/> instance with
///         <c>_hasTags=true</c>; merge into the accumulating list via
///         <c>AddAll</c>.</item>
///   <item><b>BYTES</b> &#x2014; single-hop metadata refresh
///         (2 bytes <c>[metadataVersion][networkHopType]</c>). Drain
///         + ignore. Phase 4 (single-hop) actually consumes it.</item>
/// </list>
/// <para>
/// <b>Phase 1.3.c result.</b> The public <c>PutAllAsync</c> contract
/// is plain <see cref="Task"/> &#x2014; per-key version tags are
/// discarded. The accumulator is still built so the dispatcher
/// consumes the chunk bytes correctly.
/// </para>
/// </remarks>
/// <param name="serviceProvider">DI scope for reader / VCOPL construction.</param>
/// <param name="logger">Severity-aligned with cppcache LOG* calls.</param>
/// <param name="tcrMessageHelper">Shared chunk-part-header decoder.</param>
/// <param name="region">Region this op runs against. Mirrors cppcache
/// <c>ChunkedPutAllResponse::m_region</c>.</param>
/// <param name="msg">Reply <see cref="TcrMessage"/> for auth-trailer /
/// pool back-refs. Nullable for the same reasons as
/// <see cref="ChunkedRemoveAllResponse"/> &#x2014; Phase 3+ (auth) /
/// Phase 4+ (single-hop) actually read it.</param>
/// <param name="list">Accumulating versioned-object-part list. Mirrors
/// cppcache <c>ChunkedPutAllResponse::m_list</c>; Phase 1.3 caller
/// leaves <c>null</c> &#x2014; per-key results are dropped on the
/// floor anyway.</param>
internal sealed class ChunkedPutAllResponse(
    IServiceProvider serviceProvider,
    ILogger<ChunkedPutAllResponse> logger,
    TcrMessageHelper tcrMessageHelper,
    ThinClientRegion region,
    TcrMessage? msg = null,
    VersionedCacheableObjectPartList? list = null) : TcrChunkedResult
{
    public override void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk)
    {
        // Mirrors cppcache ChunkedPutAllResponse::handleChunk
        // (cppcache/src/ThinClientRegion.cpp:3678-3727). Body shape
        // identical to ChunkedRemoveAllResponse.HandleChunk — same
        // three chunk classifications, same per-chunk
        // VersionedCacheableObjectPartList decode + AddAll merge. The
        // only diff is the log-message strings and the cppcache "PUTALL
        // operation" wording in the single-hop bytes branch.

        // ─── Step 1: wrap chunk bytes ──────────────────────────
        var reader = ActivatorUtilities.CreateInstance<BigEndianBinaryReader>(serviceProvider, payload);

        // ─── Step 2: read chunk part header ────────────────────
        // Peels partLen + isObj + DSCode/FixedID combo, classifies
        // the chunk into NullObject / Object / Exception / Bytes.
        // Expected leading DSCode = FixedIDByte (1-byte fixed-id
        // follows); expected partType = DSFid.VersionedObjectPartList.
        var chunkType = tcrMessageHelper.ReadChunkPartHeader(
            reader,
            DSCode.FixedIDByte,
            (int)DSFid.VersionedObjectPartList,
            nameof(ChunkedPutAllResponse),
            out var partLen,
            isLastChunk: (byte)(isLastChunk ? 1 : 0));

        // ─── Step 3a: NULL_OBJECT branch ───────────────────────
        // Server has no version info to ship (empty batch / caching
        // disabled). cppcache LOGDEBUG mirrored.
        if (chunkType == TcrMessageHelper.ChunkObjectType.NullObject)
        {
            logger.LogDebug("ChunkedPutAllResponse::handleChunk nullptr object");
            // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
            // true, isLastChunkWithSecurity). Phase 1.3 no auth →
            // security bit always 0 → no trailer bytes to consume.
            return;
        }

        // ─── Step 3b: OBJECT branch ───────────────────────────
        // cppcache constructs a fresh VersionedCacheableObjectPartList
        // per chunk, decodes via fromData, then merges into the
        // accumulating m_list via addAll. Phase 1.3 caller doesn't
        // supply `list`, so the merge is a no-op — accumulated per-key
        // results are dropped on the floor anyway (PutAllAsync returns
        // plain Task).
        if (chunkType == TcrMessageHelper.ChunkObjectType.Object)
        {
            logger.LogDebug("ChunkedPutAllResponse::handleChunk object");

            // cppcache: new VersionedCacheableObjectPartList(region, dsmemId, responseLock).
            // Phase 1.3 — endpointMemId always 0 (no single-hop);
            // responseLock not threaded through (single-task chunk drain).
            var vcObjPart = ActivatorUtilities.CreateInstance<VersionedCacheableObjectPartList>(
                serviceProvider, region);
            vcObjPart.FromData(reader);

            list?.AddAll(vcObjPart);

            // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
            // true, isLastChunkWithSecurity).
            return;
        }

        // ─── Step 3c: BYTES branch ────────────────────────────
        // Single-hop PR metadata refresh prelude: 2 raw bytes
        // [metadataVersion][networkHopType]. Drain them so the wire
        // reader stays aligned; the enqueue-for-refresh call
        // (cppcache ThinClientRegion.cpp:3713-3725) is Phase 4+ work
        // (needs ClientMetaDataService + ThinClientPoolDM.GetPool()).
        if (chunkType == TcrMessageHelper.ChunkObjectType.Bytes)
        {
            logger.LogDebug("ChunkedPutAllResponse::handleChunk BYTES PART");
            var metadataVersion = reader.ReadByte();
            logger.LogDebug(
                "ChunkedPutAllResponse::handleChunk single-hop bytes byte0 = {Byte0}",
                metadataVersion);
            var networkHopType = reader.ReadByte();

            // TODO Phase 3+ — m_msg.readSecureObjectPart(...).
            // TODO Phase 4+ — when metadataVersion != 0 and pool has
            // PRSingleHopEnabled + ClientMetaDataService, enqueue:
            //   poolDM.ClientMetaDataService.EnqueueForMetadataRefresh(
            //       region.FullPath, networkHopType);
            //   cppcache LOGFINE wording:
            //     "enqueued region <path> for metadata refresh for
            //      singlehop for PUTALL operation."
            _ = metadataVersion;
            _ = networkHopType;
            return;
        }

        // Fallthrough: ChunkObjectType.Exception (or unforeseen value).
        // cppcache flips reply.MessageType to EXCEPTION inside
        // readChunkPartHeader and lets the caller's reply switch handle
        // it; our TcrMessage record is immutable so we can't propagate
        // that way — throw and let the chunked reader unwind to
        // ThinClientRegion.PutAllAsync's EXCEPTION switch.
        _ = partLen;
        _ = msg;
        throw new GeodeException(
            $"ChunkedPutAllResponse.HandleChunk: unhandled chunkType={chunkType}.");
    }

    public override void Reset()
    {
        // Mirrors cppcache ChunkedPutAllResponse::reset
        // (cppcache/src/ThinClientRegion.cpp:3672-3676). Identical
        // 2-step body as ChunkedRemoveAllResponse.Reset — both bulk
        // ops ship only version tags, so the retry-clear path is the
        // same.

        // ─── Step 1: null + size guard ───────────────────────
        if (list is null || list.Size <= 0)
        {
            return;
        }

        // ─── Step 2: clear inner versionTags vector ONLY ─────
        // Does NOT null the _list reference, does NOT clear other
        // fields — cppcache keeps the same _list instance so retries
        // reuse the accumulator.
        list.VersionTags.Clear();
    }
}
