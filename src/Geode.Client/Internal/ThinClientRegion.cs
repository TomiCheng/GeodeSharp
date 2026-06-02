using System.Text.RegularExpressions;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Geode.Client.Services;

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
/// status: <see cref="ContainsKeyOnServerAsync"/>,
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
internal partial class ThinClientRegion(
    IServiceProvider serviceProvider,
    string name,
    RegionInternal? parent,
    RegionAttributes attributes)
    : LocalRegion(serviceProvider, name, parent, attributes)
{

    protected ThinClientBaseDM? _dm;

    readonly DmContextAccessor _dmContextAccessor = serviceProvider.GetRequiredService<DmContextAccessor>();
    readonly EventIdGenerator _eventIdGenerator = serviceProvider.GetRequiredService<EventIdGenerator>();
    readonly ILogger<ThinClientRegion> _logger = serviceProvider.GetRequiredService<ILogger<ThinClientRegion>>();
    readonly SerializationRegistry _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
    readonly SystemProperties _systemProperties = serviceProvider.GetRequiredService<SystemProperties>();
    readonly IServiceProvider _serviceProvider = serviceProvider;
    readonly TcrMessageHelper _tcrMessageHelper = ActivatorUtilities.CreateInstance<TcrMessageHelper>(serviceProvider);

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
    /// <param name="ct"></param>
    private async ValueTask<object?> DecodeValuePartAsync(TcrPart part, CancellationToken ct)
    {
        if (part.Payload.Length == 0)
        {
            // cppcache readObjectPart empty branch (TcrMessage.cpp:469-487):
            //   IsObject=0 → key absent → null
            //   IsObject=2 → empty byte[] sentinel
            //   other → wire error
            return part.IsObject switch
            {
                0 => null,
                2 => Array.Empty<byte>(),
                _ => throw new GeodeException(
                    $"Unexpected empty value part with IsObject={part.IsObject} " +
                    $"on Get '{FullPath}'."),
            };
        }

        if (part.IsObject == 1)
        {
            // Standard DSCode-tagged path. SerializationRegistry consumes
            // the DSCode byte and dispatches to the converter (NullObj
            // returns null).
            var reader = new DataInput(part.Payload);
            return await _serializationRegistry.ReadObjectAsync(reader, ct: ct);
        }

        // IsObject=0 + non-empty payload = CacheableBytes shortcut
        // (cppcache writeObjectPart's special-case). The shortcut emits
        // raw bytes (no DSCode), server side reconstructs as byte[].
        return part.Payload.ToArray();
    }

    [GeneratedRegex(@"^\s*(?:select|import)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex FullQueryRegex();

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

        var reader = new DataInput(part.Payload);
        return reader.ReadInt32();
    }


    /// <summary>
    /// Shortcut to the owning cache's <see cref="SerializationRegistry"/>;
    /// chunked-reply handlers pass it positionally into per-chunk
    /// <see cref="VersionedCacheableObjectPartList"/> ctors so the
    /// registry doesn't need a DI alias.
    /// </summary>
    internal SerializationRegistry SerializationRegistry => _serializationRegistry;

    //public override async Task ClearAsync(object? callback = null, CancellationToken ct = default)
    //{
    //    using var _ = _dmContextAccessor.BeginScope(_dm!);
    //    // Mirrors cppcache ThinClientRegion::clear (ThinClientRegion.cpp:767-808)
    //    // + TcrMessageClearRegion ctor (TcrMessage.cpp:1644-1682). Wire layout
    //    // is 2 parts (Region + EventId); callback arg + response-timeout
    //    // optional slots are skipped.
    //    _logger.LogTrace("ClearAsync: region={RegionPath}", FullPath);

    //    var (threadId, sequenceId) = _eventIdGenerator.Next();
    //    var request = await TcrMessageBuilder
    //        .Create(_serviceProvider, MessageType.ClearRegion)
    //        .AddRegionNamePart(FullPath)
    //        .AddEventIdPart(threadId, sequenceId)
    //        .BuildAsync(ct);

    //    var reply = await _dm!.SendSyncRequestAsync(request, ct: ct).ConfigureAwait(false);

    //    switch (reply.MessageType)
    //    {
    //        case MessageType.Reply:
    //            _logger.LogDebug("Region {RegionPath} clear sent to server", FullPath);
    //            return;

    //        case MessageType.Exception:
    //            throw new GeodeException(
    //                $"Server exception on Clear '{FullPath}': " +
    //                TcrMessageHelper.DecodeExceptionPreview(reply));

    //        case MessageType.ClearRegionDataError:
    //            throw new GeodeException(
    //                $"Server returned ClearRegionDataError on '{FullPath}'.");

    //        default:
    //            throw new GeodeException(
    //                $"Unexpected reply type {reply.MessageType} for Clear on '{FullPath}'.");
    //    }
    //}


    public override async Task ClearAsync(object? callbackArgument = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        _logger.LogTrace("ClearAsync: region={RegionPath}", FullPath);
        // cppcache ThinClientRegion::clear (ThinClientRegion.cpp:767-808):
        //
        //   void ThinClientRegion::clear(
        //       const std::shared_ptr<Serializable>& aCallbackArgument) {
        //     GfErrType err = GF_NOERR;
        //     err = localClearNoThrow(aCallbackArgument, CacheEventFlags::NORMAL);
        await LocalClearNoThrowAsync(callbackArgument, CacheEventFlags.Normal, ct);
        //     if (err != GF_NOERR) throwExceptionIfError("Region::clear", err);
        //
        //     /** @brief Create message and send to bridge server */
        //
        //     TcrMessageClearRegion request(new DataOutput(m_cacheImpl->createDataOutput()),
        //                                   this, aCallbackArgument,
        //                                   std::chrono::milliseconds(-1), m_tcrdm.get());
        var (threadId, sequenceId) = _eventIdGenerator.Next();
        var request = await TcrMessageBuilder
            .Create(_serviceProvider, MessageType.ClearRegion)
            .AddRegionNamePart(FullPath)
            .AddEventIdPart(threadId, sequenceId)
            .BuildAsync(ct);
        //     TcrMessageReply reply(true, m_tcrdm.get());
        //     err = m_tcrdm->sendSyncRequest(request, reply);
        var reply = await _dm!.SendSyncRequestAsync(request, ct: ct).ConfigureAwait(false);
        //     if (err != GF_NOERR) throwExceptionIfError("Region::clear", err);
        //
        //     switch (reply.getMessageType()) {
        //       case TcrMessage::REPLY:
        //         LOGFINE("Region %s clear message sent to server successfully",
        //                 m_fullPath.c_str());
        //         break;
        //       case TcrMessage::EXCEPTION:
        //         err = handleServerException("Region::clear:", reply.getException());
        //         break;
        //
        //       case TcrMessage::CLEAR_REGION_DATA_ERROR:
        //         LOGERROR("Region clear read error occurred on the endpoint %s",
        //                  m_tcrdm->getActiveEndpoint()->name().c_str());
        //         err = GF_CACHESERVER_EXCEPTION;
        //         break;
        //
        //       default:
        //         LOGERROR("Unknown message type %d during region clear",
        //                  reply.getMessageType());
        //         err = GF_MSG;
        //         break;
        //     }
        //     if (err == GF_NOERR) {
        //       err = invokeCacheListenerForRegionEvent(
        //           aCallbackArgument, CacheEventFlags::NORMAL, AFTER_REGION_CLEAR);
        //     }
        //     throwExceptionIfError("Region::clear", err);
        //   }

        switch (reply.MessageType)
        {
            case MessageType.Reply:
                _logger.LogDebug("Region {RegionPath} clear sent to server", FullPath);
                break;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Clear '{FullPath}': " +
                    TcrMessageHelper.DecodeExceptionPreview(reply));

            case MessageType.ClearRegionDataError:
                throw new GeodeException(
                    $"Server returned ClearRegionDataError on '{FullPath}'.");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Clear on '{FullPath}'.");
        }

        await InvokeCacheListenerForRegionEventAsync(callbackArgument, CacheEventFlags.Normal,
            RegionEventType.AfterRegionClear, ct);

    }

    public override async Task<bool> ContainsKeyOnServerAsync(object key, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        _logger.LogTrace("ContainsKeyOnServerAsync: region={RegionPath}, key={Key}", FullPath, key);

        var request = await TcrMessageBuilder
            .Create(_serviceProvider, MessageType.ContainsKey)
            .AddRegionNamePart(FullPath)
            .AddKeyPart(key)
            .AddInt32Part(0) // 0 = containsKey, 1 = containsValueForKey (cppcache TcrMessage.cpp:1837)
            .BuildAsync(ct);

        var reply = await _dm!.SendSyncRequestAsync(request, ct: ct).ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Response:
                {
                    var partReader = new DataInput(reply.Parts[0].Payload);
                    var value = _serializationRegistry.ReadObject(partReader);
                    if (value is bool b)
                    {
                        return b;
                    }
                    throw new GeodeException(
                        $"ContainsKeyOnServer on '{FullPath}': expected bool reply, " +
                        $"got {value?.GetType().Name ?? "null"}.");
                }

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on ContainsKeyOnServer '{FullPath}': " +
                    TcrMessageHelper.DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for ContainsKeyOnServer on '{FullPath}'.");
        }
    }

    public override async Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::existsValue
        // (cppcache/src/ThinClientRegion.cpp:555-566).
        var results = await QueryAsync(predicate, ct).ConfigureAwait(false);
        return results.Count > 0;
    }

    public override async Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::getAllNoThrow_remote
        // (ThinClientRegion.cpp:1089-1172) + TcrMessageGetAll ctor
        // (TcrMessage.cpp:2470-2502). Wire: 3 parts (Region + keys-as-
        // CacheableObjectArray + int(0) callback placeholder).
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("GetAll requires at least one key.", nameof(keys));
        }

        _logger.LogTrace("GetAllAsync: region={RegionPath}, keyCount={KeyCount}", FullPath, keys.Count);

        // Materialise to IReadOnlyList<object> so the chunked handler can
        // index by position (cppcache passes &m_keys to per-chunk VCOPL).
        var keyList = keys as IReadOnlyList<object> ?? [.. keys];

        var request = await TcrMessageBuilder
            .Create(_serviceProvider, MessageType.GetAll70)
            .AddRegionNamePart(FullPath)
            .AddValuePart(keyList.ToArray())   // CacheableObjectArray DSCode + N elements
            .AddInt32Part(0)                              // callback placeholder
            .BuildAsync(ct);

        // addToLocalCache mirrors cppcache LocalRegion::getAll_internal default
        // (caller-requested=true) AND caching-enabled. Proxy regions
        // (caching=false) collapse to false; VCOPL.FromData step 7 stays
        // skipped. Phase 4+ retrofit: flip when client-side caching ships.
        var addToLocalCache = Attributes.CachingEnabled;

        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedGetAllResponse>(
            _serviceProvider, _tcrMessageHelper, this, keyList, addToLocalCache);
        var reply = await _dm!
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Response:
                return chunkedResult.Values;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on GetAll '{FullPath}' (keyCount={keys.Count}).");

            case MessageType.GetAllDataError:
                throw new GeodeException($"Server returned GetAllDataError on '{FullPath}'.");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for GetAll on '{FullPath}'.");
        }
    }


    // Transitional: while LocalRegion.GetNoThrowAsync is still a stub, this
    // override keeps the pool-mode get working by delegating straight to the
    // wire fetch. Once GetNoThrowAsync lands (local-hit / loader / store-back),
    // remove this override — base LocalRegion.GetAsync (wrapper) →
    // GetNoThrowAsync → GetNoThrowRemoteAsync (overridden below) takes over.
    public override async Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default)
    {
        var (value, _) = await GetNoThrowRemoteAsync(key, callback, ct).ConfigureAwait(false);
        return value;
    }

    internal override async Task<(object? Value, VersionTag? VersionTag)>
        GetNoThrowRemoteAsync(object key, object? aCallbackArgument, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::getNoThrow_remote
        // (ThinClientRegion.cpp:810-850) + TcrMessageRequest ctor. Wire: 2 parts
        // (Region + Key); the optional callback-arg slot is skipped (pending the
        // callback-arg plumbing, same gap as the other wire ops).
        _logger.LogTrace("GetNoThrowRemoteAsync: region={RegionPath}, key={Key}", FullPath, key);
        var request = await TcrMessageBuilder
          .Create(_serviceProvider, MessageType.Request)
          .AddRegionNamePart(FullPath)
          .AddKeyPart(key)
          .BuildAsync(ct);

        var reply = await _dm!
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Response:
                if (reply.Parts.Count == 0)
                {
                    throw new GeodeException($"Get on '{FullPath}': Response with zero parts.");
                }
                return (await DecodeValuePartAsync(reply.Parts[0], ct), reply.VersionTag);

            case MessageType.Exception:
                throw new GeodeException($"Server exception on Get '{FullPath}': " +
                    TcrMessageHelper.DecodeExceptionPreview(reply));

            case MessageType.RequestDataError:
                throw new GeodeException($"Server returned RequestDataError on Get '{FullPath}'.");

            default:
                throw new GeodeException($"Unexpected reply type {reply.MessageType} for Get on '{FullPath}'.");
        }
    }

    /// <summary>
    /// Region init — base (overflow-to-disk persistence) + the ThinClient DM
    /// attachment. Mirrors cppcache <c>tmp-&gt;initTCR()</c> after region
    /// construction. Called by <c>GeodeCache.CreateRegionInternalAsync</c>.
    /// </summary>
    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        await base.InitializeAsync(ct).ConfigureAwait(false);
        await InitTcrAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Attach the distribution manager. Plain ThinClient builds its own
    /// <see cref="TcrDistributionManager"/>; <see cref="ThinClientPoolRegion"/>
    /// overrides to attach the pool's shared DM instead. Protected — internal
    /// init detail, only reached through <see cref="InitializeAsync"/>.
    /// </summary>
    protected virtual async Task InitTcrAsync(CancellationToken ct = default)
    {
        try
        {
            _dm = ActivatorUtilities.CreateInstance<TcrDistributionManager>(_serviceProvider);
            await _dm.InitAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while initializing region: {RegionName}", Name);
            throw;
        }
    }

    public override async Task InvalidateAsync(object key, object? callback = null, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::invalidateNoThrow_remote
        // (ThinClientRegion.cpp:852-886) + TcrMessageInvalidate ctor
        // (TcrMessage.cpp:1896-1932). Wire: 3 parts (Region + Key + EventId);
        // callback arg optional slot skipped.
        ArgumentNullException.ThrowIfNull(key);
        _logger.LogTrace("InvalidateAsync: region={RegionPath}, key={Key}", FullPath, key);

        var (threadId, sequenceId) = _eventIdGenerator.Next();
        var request = await TcrMessageBuilder
            .Create(_serviceProvider, MessageType.Invalidate)
            .AddRegionNamePart(FullPath)
            .AddKeyPart(key)
            .AddEventIdPart(threadId, sequenceId)
            .BuildAsync(ct);

        var reply = await _dm!.SendSyncRequestAsync(request, ct: ct).ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Reply:
                // cppcache REPLY branch reads versionTag here; Phase 4
                // concurrency-checks territory, dropped for now.
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Invalidate '{FullPath}': " +
                    TcrMessageHelper.DecodeExceptionPreview(reply));

            case MessageType.InvalidateError:
                throw new GeodeException(
                    $"Server returned InvalidateError on '{FullPath}'.");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Invalidate on '{FullPath}'.");
        }
    }

    public override async Task PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback = null, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::multiHopPutAllNoThrow_remote
        // (ThinClientRegion.cpp:1476-1540) + TcrMessagePutAll ctor
        // (TcrMessage.cpp:2354-2422). Wire: 5 + 2N parts (Region +
        // EventId + reserved-i32(0) + flags + count + N*(Key, Value)).
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count == 0)
        {
            throw new ArgumentException(
                "PutAll requires at least one entry.", nameof(map));
        }

        _logger.LogTrace("PutAllAsync: region={RegionPath}, entryCount={EntryCount}", FullPath, map.Count);

        // cppcache writeEventIdPart(map.size() - 1): only the base
        // (threadId, baseSeq) hits the wire; local sequence counter
        // advances by N so subsequent ops dont reuse the per-entry
        // logical ids the server derives as baseSeq+i.
        var (threadId, baseSequenceId) = _eventIdGenerator.NextRange(map.Count);

        // cppcache TcrMessage.cpp:2396-2404 flags byte:
        //   1 = EMPTY (no client-side caching), 2 = concurrency checks.
        const int FlagEmpty = 1;
        const int FlagConcurrencyChecks = 2;
        var flags = 0;
        if (!Attributes.CachingEnabled) flags |= FlagEmpty;
        if (Attributes.ConcurrencyChecksEnabled) flags |= FlagConcurrencyChecks;

        var builder = TcrMessageBuilder
            .Create(_serviceProvider, MessageType.PutAll)
            .AddRegionNamePart(FullPath)
            .AddEventIdPart(threadId, baseSequenceId)
            .AddInt32Part(0)            // reserved (cppcache writeIntPart(0))
            .AddInt32Part(flags)
            .AddInt32Part(map.Count);
        foreach (var kvp in map)
        {
            builder = builder
                .AddKeyPart(kvp.Key)
                .AddValuePart(kvp.Value);
        }
        var request = await builder.BuildAsync(ct);

        // Chunked reply  per-key version tags dropped on the floor
        // (RemoveAll/PutAll only ship tags, no values). Handler still
        // drains chunks so the reader loop terminates cleanly.
        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedPutAllResponse>(
            _serviceProvider, _tcrMessageHelper, this);
        var reply = await _dm!
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Reply:
                return;

            case MessageType.Response:
                _logger.LogDebug("PutAll on {RegionPath} responded RESPONSE", FullPath);
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on PutAll '{FullPath}' (entryCount={map.Count}).");

            case MessageType.PutDataError:
                throw new GeodeException(
                    $"Server returned PutDataError on PutAll '{FullPath}'.");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for PutAll on '{FullPath}'.");
        }
    }

    //public override async Task PutAsync(object key, object value, object? callback = null, CancellationToken ct = default)
    //{
    //    using var _ = _dmContextAccessor.BeginScope(_dm!);
    //    _logger.LogTrace("PutAsync: region={RegionPath}, key={Key}", FullPath, key);
    //    var (threadId, sequenceId) = _eventIdGenerator.Next();
    //    var request = await TcrMessageBuilder
    //     .Create(_serviceProvider, MessageType.Put)   // cppcache TcrMessage.cpp:1999 — m_msgType = TcrMessage::PUT
    //     .AddRegionNamePart(FullPath)
    //     .AddNullObjectPart()
    //     .AddInt32Part(0)
    //     .AddKeyPart(key)
    //     .AddCacheableBooleanPart(false)  // isDelta
    //     .AddValuePart(value)
    //     .AddEventIdPart(threadId, sequenceId)
    //     .BuildAsync(ct);

    //    var reply = await _dm!
    //        .SendSyncRequestAsync(request, ct: ct)
    //        .ConfigureAwait(false);


    //    switch (reply.MessageType)
    //    {
    //        case MessageType.Reply:
    //            return;

    //        case MessageType.Exception:
    //            throw new GeodeException(
    //                $"Server exception on Put '{FullPath}': " +
    //                TcrMessageHelper.DecodeExceptionPreview(reply));
    //        default:
    //            throw new GeodeException(
    //                $"Unexpected reply type {reply.MessageType} for Put on '{FullPath}'.");
    //    }
    //}

    /// <summary>
    /// Overrides the local-only no-op base to actually propagate the put
    /// to the server. Mirrors cppcache
    /// <c>ThinClientRegion::putNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:888</c>) — builds
    /// <c>TcrMessagePut</c> and dispatches through <see cref="_dm"/>.
    /// NIE stub first so the silent base no-op no longer sits behind
    /// every pool-mode <c>PutAsync</c>; translation lands when wire
    /// pipeline is filled in line-by-line.
    /// </summary>
    internal override async Task<VersionTag?> PutNoThrowRemoteAsync(
        object key,
        object? value,
        object? callbackArgument,
        bool checkDelta = true,
        CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        _logger.LogTrace("PutNoThrowRemoteAsync: region={RegionPath}, key={Key}", FullPath, key);

        var delta = false;
        var conflateEvents = _systemProperties.ConflateEvents;
        if (checkDelta && value is not null && conflateEvents != "true" && _dm!.IsDeltaEnabledOnServer)
        {
            delta = value is IDelta d && d.HasDelta();
        }

        var (threadId, sequenceId) = _eventIdGenerator.Next();
        var request = await TcrMessageBuilder
             .Create(_serviceProvider, MessageType.Put)  
             .AddRegionNamePart(FullPath)
             .AddNullObjectPart()
             .AddInt32Part(0)
             .AddKeyPart(key)
             .AddCacheableBooleanPart(delta)  
             .AddValuePart(value)
             .AddEventIdPart(threadId, sequenceId)
             .AddCallbackArgument(callbackArgument)
             .BuildAsync(ct);

        var reply = await _dm!
            .SendSyncRequestAsync(request, ct: ct)
            .ConfigureAwait(false);
        if (delta)
        {
            // Does not check whether success of failure..
            _cachePerfStats.DeltaPut();

            if (reply.MessageType == MessageType.PutDeltaError)
            {
                request = await TcrMessageBuilder
                    .Create(_serviceProvider, MessageType.Put)
                    .AddRegionNamePart(FullPath)
                    .AddNullObjectPart()
                    .AddInt32Part(0)
                    .AddKeyPart(key)
                    .AddCacheableBooleanPart(false)
                    .AddValuePart(value)
                    .AddEventIdPart(threadId, sequenceId)
                    .AddCallbackArgument(callbackArgument)
                    .BuildAsync(ct);
                reply = await _dm!
                    .SendSyncRequestAsync(request, ct: ct)
                    .ConfigureAwait(false);
            }
        }
        return reply.MessageType switch
        {
            MessageType.Reply => reply.VersionTag,
            MessageType.Exception => throw new GeodeException($"Server exception on Put '{FullPath}': " +
                                TcrMessageHelper.DecodeExceptionPreview(reply)),
            MessageType.PutDataError => throw new GeodeException(
                                $"Server returned PutDataError on '{FullPath}'."),
            _ => throw new GeodeException(
                                $"Unexpected reply type {reply.MessageType} for Put on '{FullPath}'."),
        };
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
    public override async Task<IReadOnlyList<object>> QueryAsync(
        string predicate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            _logger.LogError("Region query predicate string is empty");
            throw new ArgumentException(
                "Region query predicate string is empty.", nameof(predicate));
        }

        _logger.LogTrace(
            "Region::query: region={RegionPath}, predicate={Predicate}",
            FullPath, predicate);

        // cppcache ThinClientRegion.cpp:524-535 — if predicate is already
        // a full OQL (starts with SELECT/IMPORT), pass through verbatim;
        // otherwise wrap as `select distinct * from <FullPath> this where <pred>`.
        // The `this` alias is required for `WHERE this = ...` /
        // `WHERE this.field` to resolve server-side.
        var oql = FullQueryRegex().IsMatch(predicate)
            ? predicate
            : $"select distinct * from {FullPath} this where {predicate}";

        // Non-pool DM routing is deferred (memory pool-only-no-non-pool).
        if (_dm is not ThinClientPoolDM poolDm)
        {
            throw new NotImplementedException(
                "Non-pool DistributionManager query routing is not implemented.");
        }

        // <object> mirrors cppcache shared_ptr<Serializable> — row type is
        // untyped at the API boundary; TypedResultAdapter short-circuits to
        // identity when the IRegion caller asks for object.
        var query = poolDm.QueryService.NewQuery<object>(oql);
        return await query.ExecuteAsync(ct).ConfigureAwait(false);
    }

    public override async Task RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::multiHopRemoveAllNoThrow_remote
        // (ThinClientRegion.cpp:1810-1863) + TcrMessageRemoveAll ctor
        // (TcrMessage.cpp:2424-2468). Wire: 5 + N parts (Region +
        // EventId + flags + NullObj(callback) + count + N*Key).
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException(
                "RemoveAll requires at least one key.", nameof(keys));
        }

        _logger.LogTrace("RemoveAllAsync: region={RegionPath}, keyCount={KeyCount}", FullPath, keys.Count);

        var (threadId, baseSequenceId) = _eventIdGenerator.NextRange(keys.Count);

        const int FlagEmpty = 1;
        const int FlagConcurrencyChecks = 2;
        var flags = 0;
        if (!Attributes.CachingEnabled) flags |= FlagEmpty;
        if (Attributes.ConcurrencyChecksEnabled) flags |= FlagConcurrencyChecks;

        var builder = TcrMessageBuilder
            .Create(_serviceProvider, MessageType.RemoveAll)
            .AddRegionNamePart(FullPath)
            .AddEventIdPart(threadId, baseSequenceId)
            .AddInt32Part(flags)
            .AddNullObjectPart()        // callback arg = null
            .AddInt32Part(keys.Count);
        foreach (var key in keys)
        {
            builder = builder.AddKeyPart(key);
        }
        var request = await builder.BuildAsync(ct);

        var chunkedResult = ActivatorUtilities.CreateInstance<ChunkedRemoveAllResponse>(
            _serviceProvider, _tcrMessageHelper, this);
        var reply = await _dm!
            .SendSyncRequestAsync(request, chunkedResult, ct: ct)
            .ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Reply:
            case MessageType.Response:
                _logger.LogDebug(
                    "RemoveAll on {RegionPath} of {KeyCount} keys acked (type={MessageType})",
                    FullPath, keys.Count, reply.MessageType);
                return;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on RemoveAll '{FullPath}' (keyCount={keys.Count}).");

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for RemoveAll on '{FullPath}'.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Strict variant (cppcache <c>Region::remove(k, v, cb)</c>). Wire-level
    /// `expectedOldValue` part carries <paramref name="value"/>; server
    /// destroys only when the current value equals it. NIE stub — body lands
    /// when the value-bearing <c>TcrMessageDestroy</c> branch ships.
    /// </remarks>
    public override Task<bool> RemoveAsync(object key, object value, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException(
            $"Strict RemoveAsync(key, value) on '{FullPath}' is not yet wired; today only the unconditional " +
            $"{nameof(RemoveExAsync)} path is implemented.");

    public override async Task<bool> RemoveExAsync(object key, object? callback = null, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
        // Mirrors cppcache ThinClientRegion::destroyNoThrow_remote
        // (ThinClientRegion.cpp:959-999) + TcrMessageDestroy ctor null-value
        // branch (TcrMessage.cpp:1974-1985). Wire: 5 parts
        // (Region + Key + NullObj(expectedOldValue) + NullObj(operation) + EventId).
        ArgumentNullException.ThrowIfNull(key);
        _logger.LogTrace("RemoveAsync: region={RegionPath}, key={Key}", FullPath, key);

        var (threadId, sequenceId) = _eventIdGenerator.Next();
        var request = await TcrMessageBuilder
            .Create(_serviceProvider, MessageType.Destroy)
            .AddRegionNamePart(FullPath)
            .AddKeyPart(key)
            .AddNullObjectPart()    // expectedOldValue = null
            .AddNullObjectPart()    // operation = null (server treats as plain DESTROY)
            .AddEventIdPart(threadId, sequenceId)
            .BuildAsync(ct);

        var reply = await _dm!.SendSyncRequestAsync(request, ct: ct).ConfigureAwait(false);

        switch (reply.MessageType)
        {
            case MessageType.Reply:
                // Reply body layout (cppcache TcrMessage.cpp:1317-1330):
                //   flags i32 + (versionTag if flags & 0x01) + prMetaData
                //   + entryNotFound i32.  Phase 1.x doesn't drive
                //   concurrency-checks so flags stays 0, no versionTag,
                //   and entryNotFound lives in the last part.
                var entryNotFound = ReadDestroyEntryNotFound(reply);
                return entryNotFound == 0;

            case MessageType.Exception:
                throw new GeodeException(
                    $"Server exception on Remove '{FullPath}': " +
                    TcrMessageHelper.DecodeExceptionPreview(reply));

            default:
                throw new GeodeException(
                    $"Unexpected reply type {reply.MessageType} for Remove on '{FullPath}'.");
        }
    }

    public override async Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default)
    {
        using var _ = _dmContextAccessor.BeginScope(_dm!);
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


    /// <inheritdoc />
    /// <remarks>
    /// Phase 1.5 skeleton — cast <c>dm</c> to <see cref="ThinClientPoolDM"/>
    /// (the sole <see cref="IPool"/> implementor) and return it. Stays
    /// NIE until <see cref="QueryAsync"/>-style non-pool guarding lands.
    /// </remarks>
    public override IPool Pool =>
        throw new NotImplementedException(
            $"IRegion.Pool on '{FullPath}' is not yet wired; cast `dm` to ThinClientPoolDM when non-pool guarding lands.");

}
