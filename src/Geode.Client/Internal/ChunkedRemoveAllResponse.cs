/*
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="TcrChunkedResult"/> consumer for the chunked reply of a
/// <see cref="MessageType.RemoveAll"/> request. Mirrors cppcache
/// <c>ChunkedRemoveAllResponse</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:588-610</c> +
/// <c>cppcache/src/ThinClientRegion.cpp:3736-3786</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.3.b status: empty skeleton.</b> Both
/// <see cref="HandleChunk"/> and <see cref="Reset"/> throw
/// <see cref="NotImplementedException"/>. The decoder body lands once
/// <see cref="Protocol.VersionedCacheableObjectPartList"/> exists
/// (step 5 on the Phase 1.3.b todo).
/// </para>
/// <para>
/// <b>Expected payload per chunk.</b> cppcache <c>handleChunk</c>
/// distinguishes three shapes via <c>TcrMessageHelper::readChunkPartHeader</c>:
/// </para>
/// <list type="bullet">
///   <item><b>NULL_OBJECT</b> &#x2014; server has no result to ship
///         (empty batch or caching disabled). No accumulation, just
///         consume the secure-object trailer and return.</item>
///   <item><b>OBJECT</b> (<c>FixedIDByte</c> +
///         <c>DSFid.VersionedObjectPartList</c>) &#x2014; one
///         <c>VersionedCacheableObjectPartList</c> instance; merge into
///         the accumulating list via <c>addAll</c>.</item>
///   <item><b>BYTES</b> (single-hop metadata refresh) &#x2014; 2-byte
///         payload <c>[metadataVersion][networkHopType]</c>; trigger a
///         PR metadata refresh. Phase 4 (single-hop) work; Phase 1.3
///         ignores this branch.</item>
/// </list>
/// <para>
/// <b>Phase 1.3 result.</b> The public <c>RemoveAllAsync</c> contract
/// is plain <see cref="Task"/> &#x2014; per-key version tags / miss
/// flags are discarded. The accumulating list is still built so the
/// dispatcher consumes the chunk bytes correctly; surfacing it lands
/// when client-side caching does (Phase 4+).
/// </para>
/// </remarks>
/// <param name="region">
/// Region this chunked op is against. Mirrors cppcache
/// <c>ChunkedRemoveAllResponse::m_region</c>
/// (<c>std::shared_ptr&lt;Region&gt;</c>).
/// </param>
/// <param name="msg">
/// The reply <see cref="TcrMessage"/> the handler reads
/// auth-trailer / pool / endpoint-mem-id off of. Mirrors cppcache
/// <c>ChunkedRemoveAllResponse::m_msg</c> (<c>TcrMessage&amp;</c>).
/// </param>
/// <remarks>
/// Nullable in our port: cppcache constructs the reply ref
/// <i>before</i> the send and mutates it in place; our chunked
/// path synthesises the reply <i>after</i> the loop. The field is
/// kept for cppcache-shape parity but Phase 1.3 leaves it
/// <c>null</c> &#x2014; the helpers cppcache reads off it
/// (<c>getPool</c> / <c>getChunkedResultHandler</c> /
/// <c>readSecureObjectPart</c>) are all Phase 3+ (auth) / Phase 4+
/// (single-hop) territory.
/// </remarks>
/// <param name="list">
/// Accumulating list of per-key (version, miss-flag) entries.
/// Mirrors cppcache
/// <c>ChunkedRemoveAllResponse::m_list</c>
/// (<c>std::shared_ptr&lt;VersionedCacheableObjectPartList&gt;</c>).
/// </param>
/// <remarks>
/// Empty-shell type until the wire decoder lands (Phase 4+).
/// Phase 1.3 leaves the field present but unread &#x2014; per-key
/// result is dropped on the floor.
/// </remarks>
internal sealed class ChunkedRemoveAllResponse(
    IServiceProvider serviceProvider,
    ILogger<ChunkedRemoveAllResponse> logger,
    TcrMessageHelper tcrMessageHelper,
    ThinClientRegion region,
    TcrMessage? msg = null,
    VersionedCacheableObjectPartList? list = null) : TcrChunkedResult
{
    public override void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk)
    {
        // Mirrors cppcache ChunkedRemoveAllResponse::handleChunk
        // (cppcache/src/ThinClientRegion.cpp:3736-3786).

        // ─── Step 1: wrap chunk bytes ──────────────────────────
        // cppcache: cacheImpl->createDataInput(chunk, chunkLen, pool).
        // pool / cacheImpl back-refs aren't needed yet (Phase 4+ when
        // single-hop / PDX type resolution lands).
        var reader = ActivatorUtilities.CreateInstance<BigEndianBinaryReader>(serviceProvider, payload);

        // ─── Step 2: read chunk part header ────────────────────
        // Peels partLen + isObj + DSCode/FixedID combo, classifies
        // the chunk into NullObject / Object / Exception / Bytes.
        // Expected leading DSCode = FixedIDByte (1-byte fixed-id
        // follows); expected partType = DSFid.VersionedObjectPartList.
        //
        // The flags byte is reconstructed from the bool isLastChunk
        // (Phase 1.3 no auth → security bit always 0); Phase 3
        // should change TcrChunkedResult.HandleChunk's signature to
        // carry the raw flags byte instead.
        var chunkType = tcrMessageHelper.ReadChunkPartHeader(
            reader,
            DSCode.FixedIDByte,
            (int)DSFid.VersionedObjectPartList,
            nameof(ChunkedRemoveAllResponse),
            out var partLen,
            isLastChunk: (byte)(isLastChunk ? 1 : 0));

        // ─── Step 3a: NULL_OBJECT branch ───────────────────────
        // Server has no result (empty batch / caching disabled). No
        // accumulation; just consume the secure-object trailer and
        // return. cppcache LOGDEBUG mirrored.
        if (chunkType == TcrMessageHelper.ChunkObjectType.NullObject)
        {
            logger.LogDebug("ChunkedRemoveAllResponse::handleChunk nullptr object");
            // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
            // true, isLastChunkWithSecurity). Phase 1.3 no auth →
            // security bit always 0 → no trailer bytes to consume.
            return;
        }

        // ─── Step 3b: OBJECT branch ───────────────────────────
        // cppcache constructs a fresh VersionedCacheableObjectPartList
        // per chunk, decodes via fromData, then merges into the
        // accumulating m_list via addAll. Both decoder + merge NIE
        // today (Phase 4+).
        if (chunkType == TcrMessageHelper.ChunkObjectType.Object)
        {
            logger.LogDebug("ChunkedRemoveAllResponse::handleChunk object");

            // cppcache: new VersionedCacheableObjectPartList(region, dsmemId, responseLock).
            // Phase 1.3 — endpointMemId always 0 (no single-hop);
            // responseLock not threaded through (single-task chunk drain).
            var vcObjPart = ActivatorUtilities.CreateInstance<VersionedCacheableObjectPartList>(
                serviceProvider, region);
            vcObjPart.FromData(reader);

            // Phase 1.3 caller doesn't supply `list`, so the merge is
            // a no-op — accumulated per-key results are dropped on the
            // floor anyway (RemoveAllAsync returns plain Task).
            list?.AddAll(vcObjPart);

            // TODO Phase 3+ — m_msg.readSecureObjectPart(reader, false,
            // true, isLastChunkWithSecurity).
            return;
        }

        // ─── Step 3c: BYTES branch ────────────────────────────
        // Single-hop PR metadata refresh prelude: 2 raw bytes
        // [metadataVersion][networkHopType]. Drain them so the wire
        // reader stays aligned; the enqueue-for-refresh call
        // (cppcache ThinClientRegion.cpp:3777-3784) is Phase 4+ work
        // (needs ClientMetaDataService + ThinClientPoolDM.GetPool()).
        if (chunkType == TcrMessageHelper.ChunkObjectType.Bytes)
        {
            logger.LogDebug("ChunkedRemoveAllResponse::handleChunk BYTES PART");
            var metadataVersion = reader.ReadByte();
            logger.LogDebug(
                "ChunkedRemoveAllResponse::handleChunk single-hop bytes byte0 = {Byte0}",
                metadataVersion);
            var networkHopType = reader.ReadByte();

            // TODO Phase 3+ — m_msg.readSecureObjectPart(...).
            // TODO Phase 4+ — when metadataVersion != 0 and pool has
            // PRSingleHopEnabled + ClientMetaDataService, enqueue:
            //   poolDM.ClientMetaDataService.EnqueueForMetadataRefresh(
            //       region.FullPath, networkHopType);
            _ = metadataVersion;
            _ = networkHopType;
            return;
        }

        // Fallthrough: ChunkObjectType.Exception (or unforeseen
        // value). cppcache flips reply.MessageType to EXCEPTION
        // inside readChunkPartHeader and lets the caller's reply
        // switch handle it; our TcrMessage record is immutable so we
        // can't propagate that way — throw and let the chunked
        // reader unwind to ThinClientRegion.RemoveAllAsync's
        // EXCEPTION switch.
        _ = partLen;
        _ = region;
        _ = msg;
        throw new GeodeException(
            $"ChunkedRemoveAllResponse.HandleChunk: unhandled chunkType={chunkType}.");
    }

    public override void Reset()
    {
        // Mirrors cppcache ChunkedRemoveAllResponse::reset
        // (cppcache/src/ThinClientRegion.cpp:3729-3733).

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

*/