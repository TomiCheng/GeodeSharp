using System.Text;
using System.Text.RegularExpressions;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Concrete proxy-mode region implementation. Mirrors cppcache
/// <c>ThinClientRegion</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:51</c>): inherits the local
/// machinery (here: empty placeholder
/// <see cref="LocalRegion"/> / <see cref="RegionInternal"/>) and adds
/// server roundtrips via a <see cref="ThinClientBaseDM"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1.2 status: <see cref="ContainsKeyAsync"/>,
/// <see cref="PutAsync"/>, <see cref="GetAsync"/>, and
/// <see cref="RemoveAsync"/> are all end-to-end on the wire.
/// </para>
/// <para>
/// Note the type is non-generic — <c>TKey, TValue</c> live only on
/// the public <see cref="IRegion{TKey, TValue}"/> view, exposed
/// through <see cref="RegionView{TKey, TValue}"/>. The wire path
/// is <c>object</c>-typed; strong typing is compile-time only.
/// </para>
/// </remarks>
internal sealed partial class ThinClientRegion(
    IServiceProvider serviceProvider,
    ILogger<ThinClientRegion> logger,
    TcrMessageBuilder tcrMessageBuilder,
    SerializationRegistry serializationRegistry,
    EventIdGenerator eventIdGenerator,
    string name,
    CacheRegionAttributesOptions attributes,
    ThinClientBaseDM dm)
    : LocalRegion(name, null, attributes)
{

    /// <summary>
    /// Distribution manager this region dispatches to. Mirrors
    /// cppcache <c>ThinClientRegion::m_tcrdm</c>; pool-mode MVP
    /// always carries a <see cref="ThinClientPoolDM"/> here.
    /// </summary>
    internal ThinClientBaseDM DistributionManager => dm;




    public override async Task PutAsync(object key, object value, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        logger.LogTrace("PutAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::putNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:888-947) +
        // TcrMessagePut ctor (TcrMessage.cpp:1989-2034).
        //
        // ─── Step 1+2: build request frame ────────────────────
        // Region FullPath + DSCode-tagged key/value/callback via
        // SerializationRegistry; EventId pair from the per-cache
        // generator (cppcache EventIdTSS::initFromTSS). Delta is hard-
        // coded false — Phase 4 territory.
        var (threadId, sequenceId) = eventIdGenerator.Next();
        var request = tcrMessageBuilder.Put(
            regionName: FullPath,
            key: key,
            value: value,
            callbackArgument: null,
            eventThreadId: threadId,
            eventSequenceId: sequenceId);

        // ─── Step 3: dispatch via DM ─────────────────────────
        // ThinClientPoolDM.SendSyncRequestAsync picks the (single in
        // MVP) endpoint, routes through SendRequestToEndpointAsync
        // (conn borrow / fallback create / send / put-back).
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache putNoThrow_remote reply switch
        // (ThinClientRegion.cpp:928-947): Reply OK / Exception → throw
        // / PUT_DATA_ERROR → throw / anything else → throw.
        switch (reply.MessageType)
        {
            case MessageType.Reply:
                // cppcache REPLY branch reads versionTag here; we don't
                // surface version tags yet (Phase 4 concurrency checks).
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Put '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Put on '{FullPath}'.");
        }
    }

    public override async Task<object?> GetAsync(object key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        logger.LogTrace("GetAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::getNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:810-850) +
        // TcrMessageRequest ctor (TcrMessage.cpp:1858-1898).
        //
        // ─── Step 1+2: build request frame ────────────────────
        var request = tcrMessageBuilder.Get(FullPath, key);

        // ─── Step 3: dispatch via DM ─────────────────────────
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache getNoThrow_remote reply switch
        // (ThinClientRegion.cpp:826-849): Response → value /
        // Exception → throw / REQUEST_DATA_ERROR → throw /
        // anything else → throw.
        switch (reply.MessageType)
        {
            case MessageType.Response:
                if (reply.Parts.Count == 0)
                {
                    throw new GeodeException(
                        $"Get on '{FullPath}': Response with zero parts.");
                }
                return DecodeValuePart(reply.Parts[0]);

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Get '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Get on '{FullPath}'.");
        }
    }

    /// <summary>
    /// Decode a value-bearing part the way cppcache
    /// <c>TcrMessage::readObjectPart</c>
    /// (<c>cppcache/src/TcrMessage.cpp:469-487</c>) does:
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>lenObj &gt; 0</c>, <c>IsObject=1</c> → DSCode-tagged;
    ///         dispatch through <see cref="SerializationRegistry"/>
    ///         (handles NullObj internally).</item>
    ///   <item><c>lenObj &gt; 0</c>, <c>IsObject=0</c> → raw CacheableBytes
    ///         shortcut. Not used for int32 values; throw until
    ///         <c>BytesDataConverter</c> lands.</item>
    ///   <item><c>lenObj == 0</c>, <c>IsObject=2</c> → empty byte[] sentinel.
    ///         Same TODO as above.</item>
    ///   <item><c>lenObj == 0</c>, <c>IsObject=0</c> → key absent → null.</item>
    /// </list>
    /// </remarks>
    private object? DecodeValuePart(TcrPart part)
    {
        if (part.Payload.Length == 0)
        {
            // lenObj==0, isObj==0: key absent. lenObj==0, isObj==2:
            // empty byte[] (unsupported until BytesDataConverter).
            return part.IsObject switch
            {
                0 => null,
                2 => throw new NotSupportedException(
                    "Empty CacheableBytes (IsObject=2) reply not yet supported; " +
                    "needs BytesDataConverter (Phase 1.2.c)."),
                _ => throw new GeodeException(
                    $"Unexpected empty value part with IsObject={part.IsObject} " +
                    $"on Get '{FullPath}'."),
            };
        }

        if (part.IsObject == 1)
        {
            // Standard DSCode-tagged path. Registry consumes the DSCode
            // byte and dispatches to the converter (NullObj returns null).
            var reader = new BigEndianBinaryReader(part.Payload);
            return serializationRegistry.ReadObject(reader);
        }

        // IsObject=0 with non-empty payload = CacheableBytes shortcut
        // (cppcache writeObjectPart's special-case). Phase 1.2 only
        // exercises int32 values which always use IsObject=1; the
        // shortcut path stays NIE until BytesDataConverter lands.
        throw new NotSupportedException(
            $"Get '{FullPath}': IsObject=0 raw-bytes shortcut reply " +
            "not yet supported (needs BytesDataConverter, Phase 1.2.c).");
    }

    public override async Task<bool> RemoveAsync(object key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        logger.LogTrace("RemoveAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::destroyNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:959-999) +
        // TcrMessageDestroy ctor value=null branch
        // (TcrMessage.cpp:1934-1986).
        //
        // ─── Step 1+2: build request frame ────────────────────
        var (threadId, sequenceId) = eventIdGenerator.Next();
        var request = tcrMessageBuilder.Destroy(
            regionName: FullPath,
            key: key,
            eventThreadId: threadId,
            eventSequenceId: sequenceId);

        // ─── Step 3: dispatch via DM ─────────────────────────
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache destroyNoThrow_remote reply switch
        // (ThinClientRegion.cpp:973-998):
        //   REPLY → check entryNotFound flag → success xor "not found"
        //   EXCEPTION → throw
        //   DESTROY_DATA_ERROR → throw
        //   default → throw
        switch (reply.MessageType)
        {
            case MessageType.Reply:
                {
                    // Reply body layout for Destroy (cppcache
                    // TcrMessage.cpp:1317-1330):
                    //   Part flags         i32   (always present)
                    //   Part versionTag    var   (only if flags & 0x01)
                    //   Part prMetaData    1-2 bytes
                    //   Part entryNotFound i32   (0 = destroyed, 1 = absent)
                    //
                    // Phase 1.2 doesn't drive concurrency checks (flags
                    // stays 0 so no versionTag), so the entryNotFound
                    // part is the last in the list — that's the
                    // contract we read against until version-tag
                    // handling lands and we walk parts in order.
                    var entryNotFound = ReadDestroyEntryNotFound(reply);
                    return entryNotFound == 0;
                }

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Remove '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Remove on '{FullPath}'.");
        }
    }

    /// <summary>
    /// Extract the <c>entryNotFound</c> i32 from a Destroy
    /// <see cref="MessageType.Reply"/>. Mirrors cppcache
    /// <c>readIntPart</c> applied to the trailing Part
    /// (<c>cppcache/src/TcrMessage.cpp:1327</c>): a 4-byte i32 with
    /// <c>IsObject=0</c> — 0 means the entry was destroyed, 1 means
    /// the server didn't have it.
    /// </summary>
    /// <remarks>
    /// We grab the last part rather than indexing positionally because
    /// the optional version-tag part can shift indices, and Phase 1.2
    /// never reads version tags. When concurrency checks land we'll
    /// walk parts in declaration order (flags → versionTag? →
    /// prMetaData → entryNotFound) and this helper goes away.
    /// </remarks>
    private static int ReadDestroyEntryNotFound(TcrMessage reply)
    {
        if (reply.Parts.Count == 0)
        {
            throw new GeodeException(
                "Destroy Reply: no parts — expected at least the " +
                "entryNotFound int part.");
        }

        var part = reply.Parts[^1];
        if (part.Payload.Length != 4)
        {
            throw new GeodeException(
                $"Destroy Reply: expected 4-byte entryNotFound int " +
                $"part, got {part.Payload.Length} bytes.");
        }

        var reader = new BigEndianBinaryReader(part.Payload);
        return reader.ReadInt32();
    }

    public override async Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default)
    {
        logger.LogTrace("ContainsKeyAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::containsKeyOnServer
        // (cppcache/src/ThinClientRegion.cpp:676-720) +
        // TcrMessageContainsKey ctor (TcrMessage.cpp:1808-1843).
        //
        // ─── Step 1+2: build request frame ────────────────────
        // Region FullPath + DSCode-tagged key via
        // SerializationRegistry; partial source:
        // Protocol/TcrMessageBuilder.ContainsKey.cs.
        var request = tcrMessageBuilder.ContainsKey(FullPath, key);

        // ─── Step 3: dispatch via DM ─────────────────────────
        // ThinClientPoolDM.SendSyncRequestAsync picks the (single in
        // MVP) endpoint, routes through SendRequestToEndpointAsync
        // (conn borrow / fallback create / send / put-back).
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache containsKeyOnServer reply switch
        // (ThinClientRegion.cpp:691-712): Response → bool, Exception
        // → throw, anything else → throw.
        switch (reply.MessageType)
        {
            case MessageType.Response:
                {
                    // Part 0 payload = [DSCode.CacheableBoolean][0/1].
                    // Registry consumes the DSCode and dispatches to
                    // BooleanDataConverter for the 1-byte body.
                    var partReader = new BigEndianBinaryReader(reply.Parts[0].Payload);
                    var value = serializationRegistry.ReadObject(partReader);
                    if (value is bool b)
                    {
                        return b;
                    }
                    throw new GeodeException(
                        $"ContainsKey on '{FullPath}': expected bool reply, " +
                        $"got {value?.GetType().Name ?? "null"}.");
                }

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on ContainsKey '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for ContainsKey on '{FullPath}'.");
        }
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        logger.LogTrace("ClearAsync: region={RegionPath}", FullPath);

        // Mirrors cppcache ThinClientRegion::clear
        // (cppcache/src/ThinClientRegion.cpp:767-808) +
        // TcrMessageClearRegion ctor (TcrMessage.cpp:1644-1682).
        // localClearNoThrow + post-clear listener invocation are
        // local-cache machinery — Phase 2+ when caching-enabled lands.
        //
        // ─── Step 1+2: build request frame ────────────────────
        var (threadId, sequenceId) = eventIdGenerator.Next();
        var request = tcrMessageBuilder.ClearRegion(
            regionName: FullPath,
            eventThreadId: threadId,
            eventSequenceId: sequenceId);

        // ─── Step 3: dispatch via DM ─────────────────────────
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache clear reply switch
        // (ThinClientRegion.cpp:782-802):
        //   REPLY                    → success + LogDebug breadcrumb
        //   EXCEPTION                → throw
        //   CLEAR_REGION_DATA_ERROR  → throw (cppcache LogError "endpoint X")
        //   default                  → throw
        switch (reply.MessageType)
        {
            case MessageType.Reply:
                logger.LogDebug(
                    "Region {RegionPath} clear message sent to server successfully",
                    FullPath);
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Clear '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            case MessageType.ClearRegionDataError:
                logger.LogError(
                    "Region clear read error occurred on endpoint for region {RegionPath}",
                    FullPath);
                throw new GeodeException(
                    $"Server returned ClearRegionDataError on '{FullPath}'.");

            default:
                logger.LogError(
                    "Unknown message type {MessageType} during region clear on {RegionPath}",
                    reply.MessageType, FullPath);
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Clear on '{FullPath}'.");
        }
    }

    public override async Task InvalidateAsync(object key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        logger.LogTrace("InvalidateAsync: region={RegionPath}, key={Key}", FullPath, key);

        // Mirrors cppcache ThinClientRegion::invalidateNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:852-886) +
        // TcrMessageInvalidate ctor (TcrMessage.cpp:1896-1932).
        //
        // ─── Step 1+2: build request frame ────────────────────
        var (threadId, sequenceId) = eventIdGenerator.Next();
        var request = tcrMessageBuilder.Invalidate(
            regionName: FullPath,
            key: key,
            eventThreadId: threadId,
            eventSequenceId: sequenceId);

        // ─── Step 3: dispatch via DM ─────────────────────────
        var reply = await dm
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache invalidateNoThrow_remote reply switch
        // (ThinClientRegion.cpp:865-884):
        //   REPLY            → success (versionTag dropped Phase 1.2-style)
        //   EXCEPTION        → throw
        //   INVALIDATE_ERROR → throw
        //   default          → throw
        switch (reply.MessageType)
        {
            case MessageType.Reply:
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Invalidate '{FullPath}': " +
                    DecodeExceptionPreview(reply));

            case MessageType.InvalidateError:
                throw new GeodeException(
                    $"Server returned InvalidateError on '{FullPath}'.");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Invalidate on '{FullPath}'.");
        }
    }

    public override async Task RemoveAllAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException(
                "RemoveAll requires at least one key.", nameof(keys));
        }

        logger.LogTrace(
            "RemoveAllAsync: region={RegionPath}, keyCount={KeyCount}",
            FullPath, keys.Count);

        // Mirrors cppcache ThinClientRegion::multiHopRemoveAllNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:1810-1863) +
        // TcrMessageRemoveAll ctor (TcrMessage.cpp:2424-2468).

        // ─── Step 1+2: build request frame ────────────────────
        // EventIdGenerator.NextRange reserves N consecutive seq ids in
        // one Interlocked op so the server can dedup each key's event
        // as (clientId, threadId, baseSeq+i) for i ∈ [0, N).
        // cppcache writeEventIdPart(keys.size()-1) parity.
        var (threadId, baseSequenceId) = eventIdGenerator.NextRange(keys.Count);
        var request = tcrMessageBuilder.RemoveAll(
            regionName: FullPath,
            keys: keys,
            eventThreadId: threadId,
            eventSequenceId: baseSequenceId);

        // ─── Step 3: register chunked-result + dispatch ──────
        // cppcache hangs a fresh ChunkedRemoveAllResponse off the
        // TcrMessageReply via setChunkedResultHandler before the send;
        // our DM overload takes the handler directly. Phase 1.3 drops
        // per-key version tags / miss flags on the floor, but the
        // handler still has to drain chunk bodies so the reader loop
        // terminates cleanly.
        //
        // TODOs still pending (each throws NotImplementedException
        // today, surfaced through this call stack):
        // [ ] ThinClientPoolDM.SendSyncRequestAsync(req, handler, ...)
        //     body — currently NIE; needs SelectEndpoint → AddEP →
        //     SendRequestToEndpointAsync(req, handler, ep, ct).
        // [ ] SendRequestToEndpointAsync chunked overload — borrow
        //     conn → TcrConnection.SendRequestAsync(req, handler, ct)
        //     → put-back / disconnect-on-error.
        // [ ] ChunkedRemoveAllResponse.HandleChunk / Reset — currently
        //     NIE; needs VersionedCacheableObjectPartList decoder
        //     (Phase 1.3.b step 5).
        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedRemoveAllResponse>(serviceProvider, this);
        var reply = await dm
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache reply switch (ThinClientRegion.cpp:1841-1862):
        //   REPLY     → success (cppcache's "no chunks needed" branch)
        //   RESPONSE  → success (chunks already consumed by handler)
        //   EXCEPTION → throw
        //   default   → throw
        switch (reply.MessageType)
        {
            case MessageType.Reply:
            case MessageType.Response:
                logger.LogDebug(
                    "Region {RegionPath} removeAll of {KeyCount} keys acked by server " +
                    "(type={MessageType})",
                    FullPath, keys.Count, reply.MessageType);
                return;

            case MessageType.Exception:
                // cppcache surfaces the server-side exception text via
                // reply.getException(); our chunked path leaves
                // exception bytes inside the handler (Phase 1.3 doesn't
                // decode them — the handler is RemoveAll-shaped). For
                // now we throw with just the message type; surfacing
                // exception text lands when an integration test
                // demands it.
                throw new GeodeException(
                    $"Server exception on RemoveAll '{FullPath}' " +
                    $"(keyCount={keys.Count}).");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for RemoveAll on '{FullPath}'.");
        }
    }

    public override async Task PutAllAsync(IReadOnlyDictionary<object, object> map, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count == 0)
        {
            throw new ArgumentException(
                "PutAll requires at least one entry.", nameof(map));
        }

        logger.LogTrace(
            "PutAllAsync: region={RegionPath}, entryCount={EntryCount}",
            FullPath, map.Count);

        // Mirrors cppcache ThinClientRegion::multiHopPutAllNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:1476-1540) +
        // TcrMessagePutAll ctor (TcrMessage.cpp:2354-2422).

        // ─── Step 1: reserve N event ids ──────────────────────
        // cppcache writeEventIdPart(map.size() - 1): only one
        // (threadId, baseSeq) pair goes on the wire, but the
        // per-thread sequence counter is bumped by N-1 extra slots so
        // the server can dedup each entry's logical event as
        // (clientId, threadId, baseSeq+i) for i ∈ [0, N). Same scheme
        // as RemoveAll — NextRange does the Interlocked.Add(N) under
        // the hood.
        var (threadId, baseSequenceId) = eventIdGenerator.NextRange(map.Count);

        // ─── Step 2: build request frame ──────────────────────
        // 5+2N parts (region / eventId / skipCallbacks=0 / flags=0 /
        // count / N×(key,value)); see TcrMessageBuilder.PutAll.cs
        // for the layout discussion.
        var request = tcrMessageBuilder.PutAll(
            regionName: FullPath,
            map: map,
            eventThreadId: threadId,
            eventSequenceId: baseSequenceId);

        // ─── Step 3: register chunked-result + dispatch ──────
        // cppcache hangs a fresh ChunkedPutAllResponse off the
        // TcrMessageReply via setChunkedResultHandler before the send;
        // our DM overload takes the handler directly. Phase 1.3 drops
        // per-key version tags on the floor, but the handler still has
        // to drain chunk bodies so the reader loop terminates cleanly.
        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedPutAllResponse>(serviceProvider, this);
        var reply = await dm
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache reply switch (ThinClientRegion.cpp:1512-1538):
        //   REPLY           → success, no log
        //   RESPONSE        → success + LogDebug breadcrumb
        //   EXCEPTION       → throw GeodeException (cppcache handleServerException)
        //   PUT_DATA_ERROR  → throw GeodeException (cppcache GF_CACHESERVER_EXCEPTION)
        //   default         → throw GeodeException (cppcache LogError "Unknown message type")
        switch (reply.MessageType)
        {
            case MessageType.Reply:
                return;

            case MessageType.Response:
                logger.LogDebug(
                    "multiHopPutAllNoThrow_remote TcrMessage::RESPONSE {RegionPath}",
                    FullPath);
                return;

            case MessageType.Exception:
                // cppcache surfaces server-side exception text via
                // reply.getException(); our chunked path leaves exception
                // bytes inside the handler (Phase 1.3 doesn't decode them
                // — same gap as RemoveAll). Throw with the message type;
                // surfacing exception text lands when an integration test
                // demands it.
                throw new GeodeException(
                    $"Server exception on PutAll '{FullPath}' "
                    + $"(entryCount={map.Count}).");

            case MessageType.PutDataError:
                throw new GeodeException(
                    $"Server returned PutDataError on PutAll '{FullPath}'.");

            default:
                logger.LogError(
                    "Unknown message type {MessageType} during region put-all on {RegionPath}",
                    reply.MessageType, FullPath);
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for PutAll on '{FullPath}'.");
        }
    }

    public override async Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException(
                "GetAll requires at least one key.", nameof(keys));
        }

        logger.LogTrace(
            "GetAllAsync: region={RegionPath}, keyCount={KeyCount}",
            FullPath, keys.Count);

        // Mirrors cppcache ThinClientRegion::getAllNoThrow_remote
        // (cppcache/src/ThinClientRegion.cpp:1089-1172) +
        // TcrMessageGetAll ctor (TcrMessage.cpp:2470-2523).

        // ─── Step 1: materialise keys for positional access ───
        // The chunked reply indexes back into the original key list via
        // Keys[index + keysOffset] (cppcache passes &m_keys to each
        // per-chunk VersionedCacheableObjectPartList); the caller's
        // IReadOnlyCollection<object> is not indexable. Cheap fast path
        // for the common case where RegionView already produced an
        // object[] (see RegionView.GetAllAsync's boxing step).
        var keyList = keys as IReadOnlyList<object> ?? [.. keys];

        // ─── Step 2: build request frame ──────────────────────
        // 3 parts (region / keys-as-CacheableObjectArray / int(0)
        // callback placeholder); see TcrMessageBuilder.GetAll.cs for
        // the layout discussion. No EventId — GetAll has no per-key
        // mutation concept, so the EventIdGenerator isn't touched.
        var request = tcrMessageBuilder.GetAll(
            regionName: FullPath,
            keys: keyList);

        // ─── Step 3: register chunked-result + dispatch ──────
        // cppcache hangs a fresh ChunkedGetAllResponse off the
        // TcrMessageReply via setChunkedResultHandler before the send;
        // our DM overload takes the handler directly. The handler
        // accumulates the per-key result into its Values dict across
        // all chunks; we read it back after dispatch returns.
        //
        // addToLocalCache semantics mirror cppcache exactly
        // (ThinClientRegion.cpp:1100):
        //   addToLocalCache = caller-requested && caching-enabled
        // cppcache LocalRegion::getAll_internal hard-codes the
        // caller-requested side to `true` (LocalRegion.cpp:585), so the
        // effective value collapses to whatever caching-enabled is.
        // Phase 1.3 MVP regions are proxy-only (caching-enabled false /
        // null → false), so this lands at false today and the VCOPL
        // step-7 putLocal merge stays skipped. Wiring it through now
        // (not hard-coding false here) keeps the Phase 4+ retrofit a
        // one-line attribute flip instead of a call-graph edit.
        //
        // updateCountMap / destroyTracker — same Phase 4+ (client-side
        // caching) concerns; cppcache populates them ahead of the
        // request and prunes after, but only when addToLocalCache &&
        // !concurrencyChecksEnabled. Phase 1.3 skips both.
        const bool addToLocalCacheRequested = true; // cppcache LocalRegion::getAll_internal default
        var addToLocalCache = addToLocalCacheRequested
            && (Attributes.CachingEnabled ?? false); // null = unspecified, treat as false (cppcache default for proxy)

        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedGetAllResponse>(
            serviceProvider, this, keyList, addToLocalCache);
        var reply = await dm
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        // ─── Step 4: reply decoding ──────────────────────────
        // cppcache reply switch (ThinClientRegion.cpp:1148-1170):
        //   RESPONSE           → success (chunks already populated Values)
        //   EXCEPTION          → throw GeodeException
        //   GET_ALL_DATA_ERROR → throw GeodeException (LogError "endpoint X")
        //   default            → throw GeodeException (LogError "Unknown")
        switch (reply.MessageType)
        {
            case MessageType.Response:
                break;

            case MessageType.Exception:
                // cppcache surfaces server-side exception text via
                // reply.getException(); our chunked path leaves the
                // exception bytes inside the handler (Phase 1.3 doesn't
                // decode them — same gap as PutAll / RemoveAll).
                throw new GeodeException(
                    $"Server exception on GetAll '{FullPath}' "
                    + $"(keyCount={keys.Count}).");

            case MessageType.GetAllDataError:
                logger.LogError(
                    "Region get-all: a read error occurred on the endpoint for region {RegionPath}",
                    FullPath);
                throw new GeodeException(
                    $"Server returned GetAllDataError on '{FullPath}'.");

            default:
                logger.LogError(
                    "Unknown message type {MessageType} during region get-all on {RegionPath}",
                    reply.MessageType, FullPath);
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for GetAll on '{FullPath}'.");
        }

        // ─── Step 5: return result ───────────────────────────
        // chunkedResult.Values is Dictionary<object, object?> exposed as
        // IReadOnlyDictionary<object, object?>; the caller (RegionView /
        // user) can't mutate it after return. Missing-on-server keys
        // appear with null value (cppcache m_byteArray[i]==3 stores
        // null) — the public XML doc on IRegion.GetAllAsync calls this
        // out.
        return chunkedResult.Values;
    }

    /// <summary>
    /// Shared OQL routing for region convenience methods
    /// (<see cref="ExistsValueAsync"/> / <see cref="SelectValueAsync"/>).
    /// Mirrors cppcache <c>Region::query</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:518-553</c>): validate the
    /// predicate, build <c>select distinct * from &lt;FullPath&gt; this
    /// where &lt;predicate&gt;</c> (verbatim if predicate already starts
    /// with <c>SELECT</c>/<c>IMPORT</c>), dispatch via the pool DM's
    /// <see cref="RemoteQueryService"/>.
    /// </summary>
    /// <remarks>
    /// The <c>this</c> alias in FROM is required for <c>WHERE this = …</c>
    /// / <c>WHERE this.field</c> to resolve server-side. Non-pool DM
    /// routing is deferred (memory <c>pool-only-no-non-pool</c>).
    /// <c>&lt;object&gt;</c> mirrors cppcache
    /// <c>shared_ptr&lt;Serializable&gt;</c> — row type is untyped at the
    /// API boundary; <see cref="TypedResultAdapter"/> short-circuits to
    /// identity.
    /// </remarks>
    private async Task<IReadOnlyList<object>> QueryAsync(
        string predicate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            logger.LogError("Region query predicate string is empty");
            throw new ArgumentException(
                "Region query predicate string is empty.", nameof(predicate));
        }

        logger.LogTrace(
            "Region::query: region={RegionPath}, predicate={Predicate}",
            FullPath, predicate);

        var oql = FullQueryRegex1().IsMatch(predicate)
            ? predicate
            : $"select distinct * from {FullPath} this where {predicate}";

        if (dm is not ThinClientPoolDM poolDm)
        {
            throw new NotImplementedException(
                "Non-pool DistributionManager query routing is not implemented.");
        }

        var query = poolDm.QueryService.NewQuery<object>(oql);
        return await query.ExecuteAsync(ct).ConfigureAwait(false);
    }

    public override async Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default)
    {
        // Mirrors cppcache ThinClientRegion::existsValue
        // (cppcache/src/ThinClientRegion.cpp:555-566).
        var results = await QueryAsync(predicate, ct).ConfigureAwait(false);
        return results.Count > 0;
    }

    public override async Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default)
    {
        // Mirrors cppcache ThinClientRegion::selectValue
        // (cppcache/src/ThinClientRegion.cpp:618-631).
        var results = await QueryAsync(predicate, ct).ConfigureAwait(false);

        // cppcache: 0 → null; 1 → results[0]; >1 → QueryException
        // ("selectValue has more than one result"). Java's variant
        // includes the actual count — kept for diagnostics.
        return results.Count switch
        {
            0 => null,
            1 => results[0],
            _ => throw new GeodeException(
                $"selectValue has more than one result (got {results.Count})."),
        };
    }

    /// <summary>
    /// Best-effort ASCII preview of an Exception reply's Part 0. The
    /// server typically returns the Java exception class name +
    /// message there as a <c>CacheableASCIIString</c>; until
    /// <c>StringDataConverter</c> lands we just render printable bytes
    /// directly so the caller sees a readable hint in the
    /// <see cref="GeodeException"/> message. Mirrors the diagnostic
    /// pattern in <c>GetDiagnosticTests</c>.
    /// </summary>
    private static string DecodeExceptionPreview(TcrMessage reply)
    {
        if (reply.Parts.Count == 0)
        {
            return "<no exception parts>";
        }

        var bytes = reply.Parts[0].Payload.Span;
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*(?:select|import)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FullQueryRegex1();
}
