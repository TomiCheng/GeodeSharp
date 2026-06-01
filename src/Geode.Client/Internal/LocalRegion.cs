using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract local-only region machinery. Mirrors cppcache
/// <c>LocalRegion</c> (<c>cppcache/src/LocalRegion.hpp:119</c>) — owns
/// the in-memory entry map (<c>m_entries</c>), name / full-path,
/// listener / writer / loader hooks, persistence manager, expiry
/// task plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1.x is proxy-only (no client-side caching), so the in-memory
/// map + callback machinery is all deferred. The class still exists in
/// the hierarchy so <c>ThinClientRegion</c> sits at the same depth as
/// cppcache; once <c>caching-enabled</c> is honoured (Phase 2+), the
/// local-cache code lands here without disturbing the derived class.
/// </para>
/// <para>
/// cppcache ctor signature: <c>(name, CacheImpl*, parentRegion,
/// RegionAttributes, CacheStatistics, enableTimeStatistics)</c>. We
/// keep <c>name</c> + <c>parent</c> + <c>attributes</c>; cache back-ref,
/// stats, and time-stats flag are deferred until something actually
/// reads them.
/// </para>
/// </remarks>
internal partial class LocalRegion : RegionInternal
{

    static readonly ObjectFactory<LocalRegion> _objectFactory
        = ActivatorUtilities.CreateFactory<LocalRegion>([typeof(string), typeof(RegionInternal), typeof(RegionAttributes)]);

    /// <summary>
    /// Logger for cppcache <c>LOGFINEST</c> / <c>LOGDEBUG</c> mirror
    /// calls inside the CRUD pipeline (race-loser / version-conflict
    /// / invalid-delta diagnostics).
    /// </summary>
    private readonly ILogger<LocalRegion> _logger;

    /// <summary>
    /// Captured DI root — handed to per-op action factories
    /// (<see cref="PutActions.Create"/> 等) so each action 拿自己的
    /// <c>ILogger&lt;TAction&gt;</c>,不再透過 <see cref="_logger"/>
    /// reach-through。
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>cppcache <c>m_attachedPool</c>: attached pool (we route via dm at <see cref="ThinClientRegion"/>).</summary>
    protected IPool? _attachedPool;

    /// <summary>cppcache <c>m_cacheImpl-&gt;getCachePerfStats()</c> (<c>CachePerfStats</c>): cache-wide perf counters (puts / creates / delta). Distinct from the per-region access/modify timestamp <see cref="_cacheStatistics"/>.</summary>
    protected CachePerfStatistics _cachePerfStats;

    /// <summary>cppcache <c>m_cacheStatistics</c> (<c>CacheStatistics</c>): per-region last-access / last-modified timestamps for idle / TTL expiry. Returned by <c>getStatistics()</c>; written by <see cref="UpdateAccessAndModifiedTime"/>.</summary>
    protected readonly CacheStatistics _cacheStatistics = new();

    /// <summary>cppcache <c>m_destroyPending</c>: region teardown in progress.</summary>
    protected bool _destroyPending;

    /// <summary>cppcache <c>m_enableTimeStatistics</c>: time-histogram flag (OTel always on; kept for parity).</summary>
    protected bool _enableTimeStatistics;

    /// <summary>cppcache <c>expiry_task_id_</c>: region-level ExpiryTask id.</summary>
    protected object? _expiryTaskId;

    /// <summary>cppcache <c>m_isPRSingleHopEnabled</c>: single-hop routing for partitioned regions (Phase 4).</summary>
    protected bool _isPrSingleHopEnabled;

    /// <summary>cppcache <c>m_listener</c>: <see cref="ICacheListener"/> (Phase 2+; null until a listener is attached).</summary>
    protected ICacheListener? _listener;

    /// <summary>cppcache <c>m_loader</c>: <see cref="ICacheLoader"/> read-through hook (null until attached).</summary>
    protected ICacheLoader? _loader;

    /// <summary>cppcache <c>m_entries</c> (<c>EntriesMap*</c>): local entry map (Phase 2+ caching-enabled). Renamed from cppcache's <c>m_entries</c> to (a) avoid clash with <see cref="IRegion.Entries(bool)"/> and (b) separate from the <see cref="EntriesMap"/> type name.</summary>
    protected Lazy<EntriesMap?> _localEntriesMap;

    /// <summary>
    /// cppcache <c>mutex_</c>: region-wide reader-writer lock
    /// (cppcache 用 <c>boost::shared_mutex</c>;C# 港用
    /// <see cref="AsyncReaderWriterLock"/> 的 Phase 1.x 降級實作 —
    /// 排他鎖偽裝成 RW lock,API shape 對齊未來真 RW 實作)。
    /// </summary>
    protected readonly AsyncReaderWriterLock _mutex = new();

    /// <summary>cppcache <c>m_persistenceManager</c>: PersistenceManager (CLAUDE.md «Not implemented»).</summary>
    protected object? _persistenceManager;

    /// <summary>
    /// cppcache <c>m_regionStats</c>: per-region Meter sink.
    /// </summary>
    protected readonly RegionStatistics _regionStats;

    /// <summary>cppcache <c>m_released</c>: dispose path completed.</summary>
    protected bool _released;

    // ── cppcache LocalRegion fields (LocalRegion.hpp:511-530, 564) ─
    // Mirror every cppcache LocalRegion member here so the surface
    // matches 1:1. Types we have a C# port for use the real type
    // (RegionStatistics / IPool); everything else parks as `object?`
    // until the feature ships. Bool flags carry their cppcache default.
    // None of these are read today — they exist so future feature
    // ports (caching, expiry, listener, tombstone, single-hop, …)
    // land as body fills against the already-present field set.

    /// <summary>cppcache <c>m_subRegions</c>: synchronized_map name → sub-region.</summary>
    protected object? _subRegions;

    /// <summary>cppcache <c>m_transactionEnabled</c>: TX support flag.</summary>
    protected bool _transactionEnabled;

    /// <summary>cppcache <c>m_writer</c>: <see cref="ICacheWriter"/> veto hook (null until attached).</summary>
    protected ICacheWriter? _writer;

    /// <summary>cppcache <c>m_tombstoneList</c>: CRDT tombstone tracking. Phase 2+ concurrency-checks 落地時 allocate。</summary>
    internal TombstoneList? _tombstoneList;

    public LocalRegion(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes) : base(attributes)
    {
        Parent = parent;
        FullPath = parent is null ? "/" + name : parent.FullPath + "/" + name;
        Name = name;
        _localEntriesMap = new Lazy<EntriesMap?>(() =>
        {
            if (attributes.CachingEnabled)
            {
                return EntriesMapFactory.CreateMap(serviceProvider, this, attributes);
            }
            return null;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        _regionStats = ActivatorUtilities.CreateInstance<RegionStatistics>(serviceProvider, FullPath);
        _logger = serviceProvider.GetRequiredService<ILogger<LocalRegion>>();
        _cachePerfStats = serviceProvider.GetRequiredService<CachePerfStatistics>();
        _serviceProvider = serviceProvider;

        // cppcache LocalRegion ctor (LocalRegion.cpp:86-95) initializes the
        // callbacks from attributes (m_listener / m_writer / m_loader). All three
        // read their field (not Attributes) on the hot path, like cppcache.
        _listener = attributes.CacheListener;
        _writer = attributes.CacheWriter;
        _loader = attributes.CacheLoader;
    }

    /// <summary>
    /// Inner helper for <see cref="ContainsKeyAsync"/>. Mirrors cppcache
    /// <c>LocalRegion::containsKey_internal</c>
    /// (<c>cppcache/src/LocalRegion.cpp:817-826</c>) — private code-org
    /// split, one caller (<c>containsKey</c>). Gates on
    /// <see cref="RegionAttributes.CachingEnabled"/> and delegates to the
    /// local entry map.
    /// </summary>
    private bool ContainsKeyInternal(object key)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        if (!Attributes.CachingEnabled)
        {
            return false;
        }
        return InternalEntriesMap.ContainsKey(key);
    }

    /// <summary>
    /// Always-local entry count. The body of cppcache
    /// <c>LocalRegion::size_remote()</c>
    /// (<c>cppcache/src/LocalRegion.cpp:611-617</c>), exposed as a
    /// non-virtual helper so <see cref="LocalCount"/>'s no-tx branch can hit
    /// it directly — mirrors cppcache's <c>LocalRegion::size_remote()</c>
    /// explicit qualifier at <c>LocalRegion.cpp:628</c>.
    /// </summary>
    private int LocalSizeRemote()
    {
        // TODO Phase 1.5: CHECK_DESTROY_PENDING — cppcache
        //   LocalRegion.cpp:612 takes a shared_lock + checks destroy
        //   pending. Region-lifecycle flag still NIE (RegionInternal
        //   .IsDestroyed); add the guard when the flag lands.

        if (Attributes.CachingEnabled)
        {
            // cppcache LocalRegion.cpp:614 — m_entries->size().
            // LocalEntriesMap is allocated in the ctor via
            // EntriesMapFactory.CreateMap when CachingEnabled flips
            // true (Phase 2+); until that wiring lands the field is
            // still null so this dereference NREs before reaching the
            // NIE on EntriesMap.Count.
            return _localEntriesMap.Value!.Count;
        }

        // Proxy / non-caching case — cppcache LocalRegion.cpp:616.
        return 0;
    }

    async Task UpdateNoThrowAsync(IRegionAction action, CancellationToken ct)
    {
        action.CheckArgs();
        await CheckDestroyPendingAsync(ct).ConfigureAwait(false);

        var txState = action.TxState;
        if (txState is not null)
        {
            if (IsLocalOp(action.EventFlags))
            {
                throw new NotSupportedException("Local-only op not supported inside a transaction.");
            }

            await action.RemoteUpdateAsync(ct);
            txState.SetDirty();
            return;
        }

        var cachingEnabled = Attributes.CachingEnabled;

        //  do not invoke the writer in case of notification/eviction or expiration
        if (_writer is not null && action.EventFlags.InvokeCacheWriter())
        {
            action.GetCallbackOldValue();
            // invokeCacheWriterForEntryEvent method has the check that if oldValue
            // is a CacheableToken then it sets it to nullptr; also determines if it
            // should be BEFORE_UPDATE or BEFORE_CREATE depending on oldValue
            if (!await InvokeCacheWriterForEntryEvent(action.Key, action.OldValue, action.Value,
                action.CallbackArgument, action.EventFlags, action.BeforeEventType, ct))
            {
                action.LogCacheWriterFailure();
                throw new CacheWriterException($"CacheWriter vetoed {action.Name} on '{FullPath}'.");
            }
        }
        bool remoteOpDone = false;
        // try the remote update; but if this fails (e.g. due to security
        // exception) do not do the local update
        // uses the technique of adding a tracking to the entry before proceeding
        // for put; if the update counter changes when the remote update completes
        // then it means that the local entry was overwritten in the meantime
        // by a notification or another thread, so we do not do the local update
        if (!action.EventFlags.IsLocal() && !action.EventFlags.IsNotification())
        {
            if (cachingEnabled && action.UpdateCount < 0 && !Attributes.ConcurrencyChecksEnabled)
            {
                // add a tracking for the entry
                if ((action.UpdateCount = _localEntriesMap.Value!.AddTrackerForEntry(action.Key, action.OldValue, action.AddIfAbsent, action.FailIfPresent, true)) < 0)
                {
                    if (action.OldValue is not null)
                    {
                        throw new EntryExistsException($"Entry already exists for key on '{FullPath}'.");
                    }
                }
            }


            // propagate the update to remote server, if any
            try
            {
                await action.RemoteUpdateAsync(ct);
            }
            catch (Exception)
            {
                if (action.UpdateCount >= 0 && !Attributes.ConcurrencyChecksEnabled)
                {
                    _localEntriesMap.Value!.RemoveTrackerForEntry(action.Key);
                }
                throw;
            }
            remoteOpDone = true;
        }

        if (!action.EventFlags.IsNotification() || GetProcessedMarker())
        {
            try
            {
                await action.LocalUpdateAsync(action.UpdateCount, remoteOpDone, ct);
            }
            catch (GfErrTypeException ex)
            {
                switch (ex.Code)
                {
                    case GfErrType.CacheEntryUpdated:
                        _logger.LogTrace(
                            "{ActionName}: did not change local value for key [{Key}] since it has been updated by another thread while operation was in progress",
                            action.Name, action.Key);
                        break;

                    case GfErrType.CacheConcurrentModificationException:
                        _logger.LogDebug(
                            "Region::localUpdate: updateNoThrow<{ActionName}> for key [{Key}] failed because the cache already contains an entry with higher version. The cache listener will not be invoked.",
                            action.Name, action.Key);
                        break;
                    case GfErrType.InvalidDelta:
                        {
                            _logger.LogDebug(
                                "Region::localUpdate: updateNoThrow<{ActionName}> for key [{Key}] failed because of invalid delta.",
                                action.Name, action.Key);
                            _cachePerfStats.DeltaMessageFailure();

                            // Get full object from server.
                            var (newValue1, versionTag1) = await GetNoThrowFullObjectAsync(null, ct);
                            if (newValue1 is not null)
                            {
                                try
                                {
                                    (action.Entry, action.OldValue, _) = await _localEntriesMap.Value!.PutAsync(action.Key, newValue1, action.UpdateCount, 0,
                                        versionTag1 ?? action.VersionTag!, ct: ct).ConfigureAwait(false);
                                }
                                catch (GfErrTypeException ex1)
                                {
                                    if (ex1.Code == GfErrType.CacheConcurrentModificationException)
                                    {
                                        _logger.LogDebug(
                                            "Region::localUpdate: updateNoThrow<{ActionName}> for key [{Key}] failed because the cache already contains an entry with higher version. The cache listener will not be invoked.",
                                            action.Name, action.Key);
                                        return;
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }
                            }
                            //         std::shared_ptr<VersionTag> versionTag1;
                            //         err = getNoThrow_FullObject(eventId, newValue1, versionTag1);
                            //         if (err == GF_NOERR && newValue1 != nullptr) {
                            //           err = m_entries->put(key, newValue1, entry, oldValue, updateCount, 0,
                            //                                versionTag1 != nullptr ? versionTag1 : versionTag);
                            //           if (err == GF_CACHE_CONCURRENT_MODIFICATION_EXCEPTION) {
                            //             LOGDEBUG(
                            //                 "Region::localUpdate: updateNoThrow<%s> for key [%s] failed because the cache already contains \
                            //               an entry with higher version. The cache listener will not be invoked.",
                            //                 TAction::name(), Utils::nullSafeToString(key).c_str());
                            //             // Cache listener won't be called in this case
                            //             return GF_NOERR;
                            //           } else if (err != GF_NOERR) {
                            //             return err;
                            //           }
                            //         }
                        }
                        break;
                }
                throw;
            }
        }
        else
        {
            action.GetCallbackOldValue();
            if (action.UpdateCount >= 0 && !Attributes.ConcurrencyChecksEnabled)
            {
                _localEntriesMap.Value!.RemoveTrackerForEntry(action.Key);
            }
        }
        if (!action.EventFlags.IsNoCallbacks())
        {
            await InvokeCacheListenerForEntryEvent(action.Key, action.OldValue, action.Value,
                action.CallbackArgument, action.EventFlags, action.AfterEventType, ct: ct);
        }
    }

      protected async Task CheckDestroyPendingAsync(CancellationToken ct = default)
    {
        await _mutex.EnterReadLockAsync(ct).ConfigureAwait(false);
        try
        {
            if (_destroyPending)
            {
                throw new RegionDestroyedException(
                    $"Region {FullPath} has been destroyed.");
            }
        }
        finally
        {
            _mutex.ExitReadLock();
        }
    }

    /// <summary>
    /// Delta-fallback hook — server-pushed delta apply 失敗時用
    /// <paramref name="eventId"/> 從 server 重抓完整物件。Mirrors
    /// cppcache <c>LocalRegion::getNoThrow_FullObject</c>
    /// (<c>cppcache/src/LocalRegion.cpp:3189-3193</c>) — base impl
    /// 永遠回 <see langword="null"/>;<c>ThinClientRegion</c>
    /// Phase 4+ delta propagation 落地時 override 做 wire round-trip。
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache 簽章兩個 out param (<c>newValue</c> + <c>versionTag</c>),
    /// C# async 不能用 <c>ref</c>/<c>out</c>,改回傳 tuple。Phase 1.x
    /// 沒訂閱通道 → 沒 server-pushed delta → 沒 <c>InvalidDelta</c>
    /// case → 這 method 永遠不被呼叫,NIE stub 純占位讓翻譯行
    /// 對得起來。
    /// </para>
    /// </remarks>
    protected virtual Task<(object? NewValue, VersionTag? VersionTag)>
        GetNoThrowFullObjectAsync(object? eventId, CancellationToken ct) =>
        throw new NotImplementedException(
            "LocalRegion.GetNoThrowFullObjectAsync: pending Phase 4+ delta propagation.");

    /// <summary>
    /// True when the subscription channel has consumed the initial-image
    /// "all caught up" marker. Mirrors cppcache
    /// <c>LocalRegion::getProcessedMarker</c>
    /// (<c>cppcache/src/LocalRegion.hpp:408</c>) — base 永遠
    /// <see langword="true"/>;<c>ThinClientHARegion</c> Phase 4+ HA /
    /// 訂閱真做時 override 拉 <c>m_processedMarker</c> +
    /// <c>!isDurableClient()</c>。Phase 1.x proxy-only 沒訂閱通道,
    /// notification 路徑進不來,base 回 true 永遠不會錯。
    /// </summary>
    protected virtual bool GetProcessedMarker() => true;

    protected async ValueTask InvokeCacheListenerForEntryEvent(object key, object? oldValue, object? newValue,
        object? callbackArgument, CacheEventFlags eventFlags, EntryEventType type, bool isLocal = false,
        CancellationToken ct = default)
    {
        if (_listener is not null)
        {
            if (oldValue is not null && CacheableToken.IsInvalid(oldValue))
            {
                oldValue = null;
            }

            var ev = new EntryEvent(this, key, oldValue, newValue, callbackArgument, eventFlags.IsNotification());
            var eventStr = "unknown";
            try
            {
                var updateStats = true;
                var listenerStart = Stopwatch.GetTimestamp();
                switch (type)
                {
                    case EntryEventType.AfterUpdate:
                        if (oldValue is not null || eventFlags.IsNotificationUpdate() || isLocal)
                        {
                            eventStr = "afterUpdate";
                            await _listener.AfterUpdateAsync(ev, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            eventStr = "afterCreate";
                            await _listener.AfterCreateAsync(ev, ct).ConfigureAwait(false);
                        }
                        break;
                    case EntryEventType.AfterCreate:
                        eventStr = "afterCreate";
                        await _listener.AfterCreateAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.AfterDestroy:
                        eventStr = "afterDestroy";
                        await _listener.AfterDestroyAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.AfterInvalidate:
                        eventStr = "afterInvalidate";
                        await _listener.AfterInvalidateAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.BeforeCreate:
                    case EntryEventType.BeforeUpdate:
                    case EntryEventType.BeforeInvalidate:
                    case EntryEventType.BeforeDestroy:
                        updateStats = false;
                        break;
                }
                if (updateStats)
                {
                    _cachePerfStats.CacheListenerCallCompleted();
                    _regionStats.ListenerCall(Stopwatch.GetElapsedTime(listenerStart));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex, "Exception in CacheListener for key {Key} ({EventStr}) on region {RegionPath}",
                    key, eventStr, FullPath);
                throw new CacheListenerException(
                    $"CacheListener.{eventStr} failed for key on region '{FullPath}'.", ex);
            }
        }
    }

    protected async ValueTask<bool> InvokeCacheWriterForEntryEvent(object key, object? oldValue, object? newValue,
        object? callbackArgument, CacheEventFlags eventFlags, EntryEventType type, CancellationToken ct = default)
    {
        var bCacheWriterReturn = true;
        if (_writer is not null)
        {
            if (oldValue is not null && CacheableToken.IsInvalid(oldValue))
            {
                oldValue = null;
            }
            var ev = new EntryEvent(this, key, oldValue, newValue, callbackArgument, eventFlags.IsNotification());
            var eventStr = "unknown";
            try
            {
                var updateStats = true;
                var writerStart = Stopwatch.GetTimestamp();
                switch (type)
                {
                    case EntryEventType.BeforeUpdate:
                        if (oldValue is not null)
                        {
                            eventStr = "beforeUpdate";
                            bCacheWriterReturn = await _writer.BeforeUpdateAsync(ev, ct).ConfigureAwait(false);
                            break;
                        }
                        eventStr = "beforeCreate";
                        bCacheWriterReturn = await _writer.BeforeCreateAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.BeforeCreate:
                        eventStr = "beforeCreate";
                        bCacheWriterReturn = await _writer.BeforeCreateAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.BeforeDestroy:
                        eventStr = "beforeDestroy";
                        bCacheWriterReturn = await _writer.BeforeDestroyAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case EntryEventType.BeforeInvalidate:
                    case EntryEventType.AfterCreate:
                    case EntryEventType.AfterUpdate:
                    case EntryEventType.AfterInvalidate:
                    case EntryEventType.AfterDestroy:
                        updateStats = false;
                        break;
                }
                if (updateStats)
                {
                    _regionStats.WriterCall(Stopwatch.GetElapsedTime(writerStart));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex, "Exception in CacheWriter::{EventStr} for key {Key} on region {RegionPath}",
                    eventStr, key, FullPath);
                bCacheWriterReturn = false;
            }
        }
        return bCacheWriterReturn;
    }

    /// <summary>
    /// Schedule the entry-level expiry task for a freshly-created (or
    /// freshly-promoted) entry. Mirrors cppcache
    /// <c>LocalRegion::registerEntryExpiryTask</c>
    /// (<c>cppcache/src/LocalRegion.hpp:558</c>).
    /// </summary>
    /// <remarks>
    /// Phase 2+ expiry plumbing: cppcache pushes an <c>ExpiryTask</c>
    /// onto <see cref="_expiryTaskId"/>'s scheduler. NIE placeholder
    /// until the scheduler + per-entry task id surface land.
    /// </remarks>
    protected void RegisterEntryExpiryTask(MapEntry entry) =>
        throw new NotImplementedException(
            "LocalRegion.RegisterEntryExpiryTask: pending Phase 2+ expiry plumbing.");

    /// <summary>
    /// Touch the region-wide last-access / last-modified timestamps.
    /// Mirrors cppcache <c>LocalRegion::updateAccessAndModifiedTime</c>
    /// (<c>cppcache/src/LocalRegion.hpp:146</c>, <c>override</c>).
    /// </summary>
    /// <remarks>
    /// Phase 2+ expiry plumbing: writes timestamps for the region-level
    /// idle / TTL expiry task. NIE placeholder until the expiry-task
    /// scheduler lands.
    /// </remarks>
    protected virtual void UpdateAccessAndModifiedTime(bool modified)
    {
        // cppcache LocalRegion::updateAccessAndModifiedTime (LocalRegion.cpp:118-139).
        // locking not required since setters use atomic operations.
        if (!RegionExpiryEnabled)
        {
            return;
        }

        // cppcache `auto now = steady_clock::now()` → Stopwatch monotonic ticks。
        //   SetLast*Time 還是 NIE — region-expiry 開啟時 (Phase 2+) 這裡會大聲
        //   響,Phase 1.x 因上面 early-return 不會到。
        var now = Stopwatch.GetTimestamp();
        _logger.LogDebug("Setting last accessed time for region {FullPath} to {Time}", FullPath, now);
        _cacheStatistics.SetLastAccessedTime(now);
        if (modified)
        {
            _logger.LogDebug("Setting last modified time for region {FullPath} to {Time}", FullPath, now);
            _cacheStatistics.SetLastModifiedTime(now);
        }

        // TODO (cppcache 自己也存疑): should we really touch the parent region??
        // cppcache dynamic_cast<RegionInternal*>(m_parentRegion) — 我們的
        //   UpdateAccessAndModifiedTime 掛在 LocalRegion (不在 RegionInternal),
        //   所以 cast 對象是 LocalRegion;ThinClientRegion 繼承它,涵蓋。protected
        //   跨 instance 同類別呼叫合法。
        if (Parent is LocalRegion parent)
        {
            parent.UpdateAccessAndModifiedTime(modified);
        }
    }

    /// <summary>
    /// Touch a single entry's last-access / last-modified timestamps —
    /// used after an update to refresh the entry's idle / TTL countdown
    /// without re-scheduling. Mirrors cppcache
    /// <c>LocalRegion::updateAccessAndModifiedTimeForEntry</c>
    /// (<c>cppcache/src/LocalRegion.hpp:556-557</c>, <c>override</c>).
    /// </summary>
    /// <remarks>
    /// Phase 2+ expiry plumbing: cppcache writes
    /// <c>entry-&gt;getExpProperties()</c>'s last-access / last-modified;
    /// reachable only when <see cref="EntryExpiryEnabled"/> is true and
    /// an existing entry already has a scheduled task. NIE placeholder
    /// until <c>MapEntry</c>'s expiry-properties surface + the scheduler
    /// land.
    /// </remarks>
    protected virtual void UpdateAccessAndModifiedTimeForEntry(MapEntry? entry, bool modified)
    {

        // cppcache LocalRegion::updateAccessAndModifiedTimeForEntry (LocalRegion.cpp:2836-2859):
        //
        //   void LocalRegion::updateAccessAndModifiedTimeForEntry(
        //       std::shared_ptr<MapEntryImpl>& ptr, bool modified) {
        //     // locking is not required since setters use atomic operations
        if (entry is not null && EntryExpiryEnabled)
        {

            //       ExpEntryProperties& expProps = ptr->getExpProperties();
            //       auto now = std::chrono::steady_clock::now();
            //       std::string keyStr;
            //       if (Log::enabled(LogLevel::Debug)) {
            //         std::shared_ptr<CacheableKey> key;
            //         ptr->getKeyI(key);
            //         keyStr = Utils::nullSafeToString(key);
            //       }
            //       LOGDEBUG("Setting last accessed time for key [%s] in region %s to %s",
            //                keyStr.c_str(), getFullPath().c_str(),
            //                to_string(now.time_since_epoch()).c_str());
            //       expProps.last_accessed(now);
            //       if (modified) {
            //         LOGDEBUG("Setting last modified time for key [%s] in region %s to %s",
            //                  keyStr.c_str(), getFullPath().c_str(),
            //                  to_string(now.time_since_epoch()).c_str());
            //         expProps.last_modified(now);
            //       }
            throw new NotImplementedException(
                "LocalRegion.UpdateAccessAndModifiedTimeForEntry: pending Phase 2+ expiry plumbing.");
        }
        //   }
    }

    protected bool EntryExpiryEnabled => Attributes.EntryExpiryEnabled;


    /// <summary>
    /// Parent region in the sub-region tree, or <see langword="null"/> for a
    /// root region. Mirrors cppcache <c>LocalRegion::m_parentRegion</c>.
    /// </summary>
    protected RegionInternal? Parent { get; }

    /// <summary>
    /// Whether the region's region-level expiry policy is configured (TTL
    /// or idle-timeout &gt; 0). Mirrors cppcache
    /// <c>RegionInternal::regionExpiryEnabled</c>
    /// (<c>cppcache/src/RegionInternal.hpp:317-319</c>) — inline
    /// non-virtual pure-getter forwarding to
    /// <see cref="RegionAttributes.RegionExpiryEnabled"/>. Sibling of
    /// <see cref="EntryExpiryEnabled"/>.
    /// </summary>
    protected bool RegionExpiryEnabled => Attributes.RegionExpiryEnabled;

    internal static LocalRegion Create(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes)
    {
        return _objectFactory(serviceProvider, [name, parent, attributes]);
    }

    /// <summary>
    /// Destroys <paramref name="key"/> locally (and, when
    /// <paramref name="eventFlags"/> distributes, on the server). Mirrors
    /// cppcache <c>LocalRegion::destroyNoThrow</c>
    /// (<c>cppcache/src/LocalRegion.hpp:298-302</c>) — sibling of
    /// <see cref="PutNoThrowAsync"/>; the body will delegate to
    /// <c>UpdateNoThrowAsync&lt;DestroyActions&gt;</c> once the destroy
    /// strategy lands. Caller today is the LRU evict path
    /// (<see cref="LRULocalDestroyAction"/>) with
    /// <c>EVICTION | LOCAL</c> flags. <paramref name="versionTag"/> is a
    /// by-value input here (cppcache passes it by value, not by-ref like
    /// <see cref="PutNoThrowRemoteAsync"/>); <see cref="Protocol.GfErrType"/>
    /// collapses to throw/void per the codebase err-code → exception
    /// convention.
    /// </summary>
    internal async Task DestroyNoThrowAsync(
        object key,
        object? callbackArgument,
        int updateCount,
        CacheEventFlags eventFlags,
        VersionTag? versionTag = null,
        CancellationToken ct = default)
    {
        var action = DestroyActions.Create(_serviceProvider, this, key, callbackArgument, updateCount, eventFlags, versionTag);
        await UpdateNoThrowAsync(action, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Propagates the destroy to the remote server, if any. Mirrors cppcache
    /// <c>LocalRegion::destroyNoThrow_remote</c>
    /// (<c>cppcache/src/LocalRegion.cpp:3070-3074</c>) — the base impl is a
    /// no-op success (<c>return GF_NOERR</c>; a pure local region has no server
    /// backing); <c>ThinClientRegion</c> overrides with the
    /// <c>TcrMessageDestroy</c> wire round-trip. Target of
    /// <see cref="DestroyActions.RemoteUpdateAsync"/>; sibling of
    /// <see cref="PutNoThrowRemoteAsync"/> (no <c>value</c> arg — destroy).
    /// </summary>
    /// <remarks>
    /// cppcache out-param <c>versionTag</c> → return value (C# async can't take
    /// <c>ref</c>/<c>out</c>); <see cref="DestroyActions.RemoteUpdateAsync"/>
    /// writes the result back to its <c>VersionTag</c> field. NIE for now:
    /// translate the base no-op (<c>return Task.FromResult((VersionTag?)null)</c>,
    /// like <see cref="PutNoThrowRemoteAsync"/>) once the destroy pipeline is
    /// wired end-to-end — until then it stays an explicit stub so the gap is
    /// visible.
    /// </remarks>
    internal virtual Task<VersionTag?> DestroyNoThrowRemoteAsync(
        object key,
        object? aCallbackArgument,
        CancellationToken ct = default)
    {
        // cppcache LocalRegion::destroyNoThrow_remote (LocalRegion.cpp:3070-3074):
        //   base no-op success (`return GF_NOERR`) — pure local region, no server;
        //   versionTag never set. ThinClientRegion overrides with the wire round-trip.
        return Task.FromResult((VersionTag?)null);
    }

    /// <summary>
    /// Core get logic: tx dispatch, local-cache hit, remote fetch,
    /// cache-loader fallback, <c>putLocal</c> store-back, listener
    /// dispatch. Mirrors cppcache <c>LocalRegion::getNoThrow</c>
    /// (<c>cppcache/src/LocalRegion.cpp:851-1026</c>). Non-virtual like
    /// cppcache — the per-mode override point is
    /// <c>GetNoThrowRemoteAsync</c> (base no-op here; ThinClientRegion
    /// fetches over the wire), not this method.
    /// </summary>
    internal async Task<object?> GetNoThrowAsync(object key, object? callbackArgument, CancellationToken ct = default)
    {
        await CheckDestroyPendingAsync(ct).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(key, nameof(key));

        var txState = GetTXState();
        if (txState is not null)
        {
            if (IsLocalOp())
            {
                throw new NotSupportedException(
                    "GetNoThrowAsync: local-op get inside a transaction is not supported.");
            }
            var (txValue, _) = await GetNoThrowRemoteAsync(key, callbackArgument, ct).ConfigureAwait(false);
            txState.SetDirty();
            if (txValue is not null && (CacheableToken.IsInvalid(txValue) || CacheableToken.IsTombstone(txValue)))
            {
                txValue = null;
            }
            return txValue;
        }

        _cachePerfStats.Get();

        // TODO:  CacheableToken::isInvalid should be completely hidden
        // inside MapSegment; this should be done both for the value obtained
        // from local cache as well as oldValue in every instance
        var updateCount = -1;
        var isLoaderInvoked = false;
        var isLocal = false;   // cppcache LocalRegion.cpp:888
        var cachingEnabled = Attributes.CachingEnabled;
        object? value;
        object? localValue = null;
        if (cachingEnabled)
        {
            var (me, localGet) = InternalEntriesMap.GetEntry(key);
            value = localGet;
            isLocal = me is not null;
            if (isLocal && value is not null && !CacheableToken.IsInvalid(value))
            {
                _regionStats.Hit();
                _cachePerfStats.Hit();

                UpdateAccessAndModifiedTimeForEntry(me, false);
                UpdateAccessAndModifiedTime(false);
                return value;
            }
            localValue = value;
            value = null;

            if (!Attributes.ConcurrencyChecksEnabled)
            {
                updateCount = InternalEntriesMap.AddTrackerForEntry(
                    key, value, addIfAbsent: true, failIfPresent: false, value: false);
                _logger.LogDebug("Region::get: added tracking with update counter {UpdateCount} for key {Key} with value {Value}",
                    updateCount, key, value);
            }
        }
        try
        {
            UpdateAccessAndModifiedTime(false);

            _regionStats.Miss();
            _cachePerfStats.Miss();

            var (remoteValue, versionTag) = await GetNoThrowRemoteAsync(key, callbackArgument, ct).ConfigureAwait(false);
            value = remoteValue;

            // cppcache reads the m_loader field (LocalRegion.cpp:948), wired
            // from attributes in the ctor — same field model as listener/writer.
            var loader = _loader;
            if ((value is null || CacheableToken.IsInvalid(value) || CacheableToken.IsTombstone(value))
                && loader is not null)
            {
                isLoaderInvoked = true;
                var loaderStart = Stopwatch.GetTimestamp();
                try
                {
                    value = await loader.LoadAsync(this, key, callbackArgument, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CacheLoader.LoadAsync for key {Key} in region {RegionPath}", key, FullPath);
                    throw new CacheLoaderException(
                        $"CacheLoader.LoadAsync failed for key on region '{FullPath}'.", ex);
                }
                finally
                {
                    _regionStats.LoaderCall(Stopwatch.GetElapsedTime(loaderStart));
                }
            }

            object? oldValue = null;
            if (value is not null && cachingEnabled
                && !(CacheableToken.IsTombstone(value)
                     && (localValue is null || CacheableToken.IsInvalid(localValue))))
            {
                _logger.LogDebug(
                    "Region::get: creating entry with tracking update counter {UpdateCount} for key {Key}",
                    updateCount, key);
                try
                {
                    oldValue = await PutLocalAsync(
                        "Region::get", isCreate: false, key, value, cachingEnabled,
                        updateCount, destroyTracker: 0, versionTag, ct: ct).ConfigureAwait(false);
                }
                catch (GfErrTypeException ex)
                {
                    if (ex.Code == GfErrType.CacheConcurrentModificationException)
                    {
                        _logger.LogDebug("Region::get: putLocal for key {Key} failed; cache already holds a higher-version entry.",
                            key);
                        if (value is not null && (CacheableToken.IsInvalid(value) || CacheableToken.IsTombstone(value)))
                        {
                            value = null;
                        }
                        return value;
                    }

                    _logger.LogDebug("Region::get: putLocal for key {Key} failed with {GfErrType}; keeping fetched value.",
                        key, ex.Code);
                }
            }

            if (value is not null && (CacheableToken.IsInvalid(value) || CacheableToken.IsTombstone(value)))
            {
                value = null;
            }

            if (!isLoaderInvoked && value is not null)
            {
                await InvokeCacheListenerForEntryEvent(
                    key, oldValue, value, callbackArgument, CacheEventFlags.Normal,
                    EntryEventType.AfterUpdate, isLocal, ct).ConfigureAwait(false);
            }

            return value;
        }
        finally
        {
            if (updateCount >= 0 && !Attributes.ConcurrencyChecksEnabled)
            {
                InternalEntriesMap.RemoveTrackerForEntry(key);
            }
        }
    }

    /// <summary>
    /// Propagate the get to the remote server, if any. Mirrors cppcache
    /// <c>LocalRegion::getNoThrow_remote</c>
    /// (<c>cppcache/src/LocalRegion.cpp:3036-3041</c>) — base impl is a
    /// no-op success: a pure local region has no server backing, so it
    /// yields <c>(null, null)</c> (no value, no version tag).
    /// <see cref="ThinClientRegion"/> overrides
    /// (<c>cppcache/src/ThinClientRegion.cpp:810-850</c>) with the
    /// <c>TcrMessageRequest</c> wire round-trip. Remote-fetch step of
    /// <see cref="GetNoThrowAsync"/>.
    /// </summary>
    /// <remarks>
    /// cppcache out-params <c>value</c> + <c>versionTag</c>
    /// (<c>std::shared_ptr&lt;T&gt;&amp;</c>) → return tuple; C# async
    /// can't take <c>ref</c>/<c>out</c>. Same shape as
    /// <see cref="GetNoThrowFullObjectAsync"/>.
    /// </remarks>
    internal virtual Task<(object? Value, VersionTag? VersionTag)>
        GetNoThrowRemoteAsync(object key, object? aCallbackArgument, CancellationToken ct = default)
        => Task.FromResult<(object?, VersionTag?)>((null, null));

    /// <summary>
    /// Current thread / async-flow's ambient transaction, or
    /// <see langword="null"/> when no tx is open. Mirrors cppcache
    /// <c>LocalRegion::getTXState()</c>
    /// (<c>cppcache/src/LocalRegion.hpp:477</c>), which delegates to
    /// <c>TSSTXStateWrapper::get().getTXState()</c>.
    /// </summary>
    internal TXState? GetTXState()
    {
        // TODO Phase 4+ (transactions): wire to TSSTXStateWrapper
        //   equivalent — likely a static AsyncLocal<TXState?> on a
        //   TSSTXStateWrapper helper, set by CacheTransactionManager
        //   .Begin / cleared by Commit / Rollback. Returning null today
        //   matches the "no transaction in progress" branch in every
        //   caller (e.g. LocalRegion::size line 619-629), so call sites
        //   can already reference this method without behavioural drift.
        return null;
    }

    /// <summary>
    /// Whether the current op is local-only — either because this region
    /// instance is a plain <see cref="LocalRegion"/> (no server backing)
    /// or because the caller flagged the op with
    /// <see cref="CacheEventFlags.Local"/>. Mirrors cppcache
    /// <c>LocalRegion::isLocalOp</c>
    /// (<c>cppcache/src/LocalRegion.hpp:482-485</c>).
    /// </summary>
    internal bool IsLocalOp(CacheEventFlags? eventFlags = null) =>
        // cppcache `typeid(*this) == typeid(LocalRegion)`: exact-type
        // (not derived) RTTI check. ThinClientRegion (and future
        // subclasses) carry a server, so they return false here.
        GetType() == typeof(LocalRegion)
        || (eventFlags is { } f && f.HasFlag(CacheEventFlags.Local));

    /// <summary>
    /// Writes the entry to the local in-memory map (create or update) and
    /// bumps region stats / expiry tasks. Mirrors cppcache
    /// <c>LocalRegion::putLocal</c>
    /// (<c>cppcache/src/LocalRegion.cpp:2471-2543</c>). Target of
    /// <see cref="PutActions.LocalUpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache out-param <c>oldValue</c>
    /// (<c>std::shared_ptr&lt;Cacheable&gt;&amp;</c>) → return value;
    /// <c>versionTag</c> / <c>delta</c> / <c>eventId</c> are inputs. The
    /// <c>GfErrType</c> return collapses: hard failures throw, while
    /// pipeline signals (<c>GF_INVALID_DELTA</c>,
    /// <c>GF_CACHE_CONCURRENT_MODIFICATION_EXCEPTION</c> from
    /// <c>m_entries-&gt;put</c>/<c>create</c>) ride
    /// <see cref="GfErrTypeException"/> for the caller to <c>switch</c>.
    /// </para>
    /// <para>
    /// Phase 2+ caching-enabled: needs <see cref="_localEntriesMap"/>
    /// (<c>EntriesMap</c> create / put) + expiry-task plumbing, both
    /// still NIE. The <c>GF_INVALID_DELTA</c> branch round-trips
    /// <see cref="GetNoThrowFullObjectAsync"/> (hence async). NIE
    /// placeholder until those land.
    /// </para>
    /// </remarks>
    internal async Task<object?> PutLocalAsync(
        string name,
        bool isCreate,
        object key,
        object? value,
        bool cachingEnabled,
        int updateCount,
        int destroyTracker,
        VersionTag? versionTag,
        DataInput? delta = null,
        object? eventId = null,
        CancellationToken ct = default)
    {
        var isUpdate = !isCreate;
        object? oldValue = null;
        if (cachingEnabled)
        {
            MapEntry? entry = null;
            _logger.LogDebug("{ActionName}: region [{FullPath}] putting key [{Key}], value [{Value}]",
                name, FullPath, key, value);
            if (isCreate)
            {
                (entry, oldValue) = await _localEntriesMap.Value!.CreateAsync(key, value!, updateCount, destroyTracker, versionTag, ct).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    (entry, oldValue, isUpdate) = await _localEntriesMap.Value!.PutAsync(key, value!, updateCount, destroyTracker, versionTag!, delta, ct).ConfigureAwait(false);
                }
                catch (GfErrTypeException ex) when (ex.Code == GfErrType.InvalidDelta)
                {
                    _cachePerfStats.DeltaMessageFailure();
                    var (newValue1, versionTag1) = await GetNoThrowFullObjectAsync(eventId, ct)
                        .ConfigureAwait(false);
                    if (newValue1 is not null)
                    {
                        (entry, oldValue, isUpdate) = await _localEntriesMap.Value!.PutAsync(key, newValue1, updateCount, destroyTracker, (versionTag1 ?? versionTag)!, ct: ct).ConfigureAwait(false);
                    }
                }
                // Means that delta is on and there is no failure.
                if (delta is not null)
                {
                    _cachePerfStats.DeltaReceived();
                }
            }

            _logger.LogDebug(
                "{ActionName}: region [{FullPath}] {Operation} key [{Key}], value [{Value}]",
                name, FullPath, isUpdate ? "updated" : "created", key, value);

            // entry/region expiration
            if (EntryExpiryEnabled)
            {
                if (isUpdate && entry!.IsExpiryTaskScheduled)
                {
                    UpdateAccessAndModifiedTimeForEntry(entry, true);
                }
                else
                {
                    RegisterEntryExpiryTask(entry!);
                }
            }
            UpdateAccessAndModifiedTime(true);
        }

        // update the stats
        if (isUpdate)
        {
            _cachePerfStats.Put();
        }
        else
        {
            _regionStats.Create();
            _cachePerfStats.Create();
        }
        return oldValue;
    }
    internal async Task PutNoThrowAsync(
        object key,
        object? value,
        object? callbackArgument,
        int updateCount,
        CacheEventFlags eventFlags,
        CancellationToken ct = default)
    {
        var put = PutActions.Create(_serviceProvider, this, key, value, callbackArgument, updateCount, eventFlags);
        await UpdateNoThrowAsync(put, ct);
    }

    /// <summary>
    /// Propagates the put to the remote server, if any. Mirrors cppcache
    /// <c>LocalRegion::putNoThrow_remote</c>
    /// (<c>cppcache/src/LocalRegion.cpp:3043-3048</c>) — the base impl is
    /// a no-op success (a pure local region has no server backing);
    /// <c>ThinClientRegion</c> overrides
    /// (<c>cppcache/src/ThinClientRegion.cpp:888</c>) with the
    /// <c>TcrMessagePut</c> wire round-trip. Target of
    /// <see cref="PutActions.RemoteUpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache out-param <c>versionTag</c>
    /// (<c>std::shared_ptr&lt;VersionTag&gt;&amp;</c>) → return value; C#
    /// async can't take <c>ref</c>/<c>out</c>. The
    /// <c>RemoteUpdateAsync</c> caller writes the result back to
    /// <c>action.VersionTag</c>. <c>checkDelta</c> keeps cppcache's
    /// <see langword="true"/> default.
    /// </para>
    /// <para>
    /// Phase 1.x proxy-only: the real put always routes through the
    /// <c>ThinClientRegion</c> override, so this base body is never
    /// reached for a server-backed region. NIE placeholder until either
    /// the override lands or a genuine local-only no-op is wired.
    /// </para>
    /// </remarks>
    internal virtual Task<VersionTag?> PutNoThrowRemoteAsync(
        object key,
        object? value,
        object? aCallbackArgument,
        bool checkDelta = true,
        CancellationToken ct = default)
    {
        return Task.FromResult((VersionTag?)null);
    }

    /// <summary>
    /// Virtual hook used by <see cref="LocalCount"/>'s in-tx branch. Default
    /// body matches the non-virtual <see cref="LocalSizeRemote"/> —
    /// <see cref="ThinClientRegion"/> overrides (Phase 1.5+) to round-trip
    /// <c>TcrMessageSize</c> to the server. Mirrors cppcache
    /// <c>LocalRegion::size_remote()</c> as the virtual dispatch target
    /// (called at <c>LocalRegion.cpp:625</c>).
    /// </summary>
    internal virtual int SizeRemote() => LocalSizeRemote();

    internal EntriesMap InternalEntriesMap => _localEntriesMap.Value!;

    /// <inheritdoc />
    internal override async Task EvictAsync(float percentage, CancellationToken ct = default)
    {
        // cppcache LocalRegion::evict (LocalRegion.cpp:3155-3171) holds a
        // boost::shared_lock for the WHOLE evict. We can't: our Phase-1.x
        // AsyncReaderWriterLock is an exclusive non-reentrant SemaphoreSlim
        // (see its remarks). The per-entry destroy chain
        //   ProcessLruAsync → EvictionHelperAsync → LRULocalDestroyAction
        //   → DestroyNoThrowAsync → UpdateNoThrowAsync → CheckDestroyPendingAsync
        // re-acquires _mutex, so holding it across ProcessLruAsync would
        // self-deadlock. So we take the lock ONLY to read the released/
        // destroyPending guard + snapshot the map reference, release it, then
        // drive ProcessLruAsync lock-free.
        //
        // Safe despite the early release: the LRUEntriesMap reference is stable
        // once the Lazy is materialised, and each per-entry destroy re-checks
        // _destroyPending itself — a region torn down mid-evict surfaces
        // RegionDestroyedException, which LRULocalDestroyAction.EvictAsync
        // swallows (returns false). This is the same lock-free-during-eviction
        // shape the entry-count LRU path already relies on (Put doesn't hold
        // _mutex across its ProcessLruAsync either).
        LRUEntriesMap? lruMap = null;
        await _mutex.EnterReadLockAsync(ct).ConfigureAwait(false);
        try
        {
            if (_released || _destroyPending)
            {
                return;
            }

            // m_entries cast → LRUEntriesMap:heap-LRU 開時 EntriesMapFactory
            //   一定建 LRUEntriesMap;wrong type / 未 materialize → silent skip。
            if (_localEntriesMap.IsValueCreated
                && _localEntriesMap.Value is LRUEntriesMap m)
            {
                lruMap = m;
            }
        }
        finally
        {
            _mutex.ExitReadLock();
        }

        if (lruMap is null)
        {
            return;
        }

        // LOGINFO → LogInformation,structured params 保留 cppcache 字面措辭。
        // processLRU(entriesToEvict) → ProcessLruAsync(int) overload。
        var size = lruMap.Count;
        var entriesToEvict = (int)(percentage * size);
        _logger.LogInformation(
            "Evicting {EntriesToEvict} entries. Current entry count is {Size}",
            entriesToEvict, size);
        await lruMap.ProcessLruAsync(entriesToEvict, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// cppcache <c>LocalRegion::release</c> 對應 — mark destroyed,
    /// dispose the local entries map(cascades into
    /// <c>LRUEntriesMap.DisposeAsync</c> →
    /// <see cref="EvictionController.UnregisterRegion"/>),最後 forward 到 base。
    /// 由 <c>GeodeCache.CloseAsync</c> 在 pool drain 之前呼叫,EC / DI scope
    /// 都還活著。Idempotent — DI scope teardown 會再 dispose 一次當安全網。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_released) return;
        _destroyPending = true;
        _released = true;

        // Lazy 未 materialize → 沒人碰過 entries map,沒東西可釋放。
        // Value 為 null → caching disabled(factory return null);也跳。
        if (_localEntriesMap.IsValueCreated && _localEntriesMap.Value is { } map)
        {
            await map.DisposeAsync().ConfigureAwait(false);
        }

        // TODO Phase 1.5+: cppcache LocalRegion::release 還會
        //   * cancel expiry tasks(RegionTimeToLive / EntryTimeToLive 經 ExpiryTaskManager)
        //   * dispose callbacks(m_listener / m_writer / m_loader 若 IDisposable)
        //   等對應子系統落地時補進來。

        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override async Task ClearAsync(object? callback = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await LocalClearAsync(callback, ct).ConfigureAwait(false);
    }

    public override async Task LocalClearAsync(object? callback = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await LocalClearNoThrowAsync(callback, CacheEventFlags.Local, ct);
    }
    
    internal async Task LocalClearNoThrowAsync(
        object? callbackArgument,
        CacheEventFlags eventFlags,
        CancellationToken ct = default)
    {
        var cachingEnabled = Attributes.CachingEnabled;
        _regionStats.Clear();

        await _mutex.EnterReadLockAsync(ct).ConfigureAwait(false);
        try
        {
            if (_released || _destroyPending)
            {
                return;
            }

            if (!await InvokeCacheWriterForRegionEventAsync(
                    callbackArgument, eventFlags, RegionEventType.BeforeRegionClear, ct).ConfigureAwait(false))
            {
                _logger.LogTrace("Cache writer prevented region clear on {RegionPath}", FullPath);
                throw new CacheWriterException($"CacheWriter vetoed Clear on '{FullPath}'.");
            }

            if (cachingEnabled)
            {
                await InternalEntriesMap.ClearAsync(ct).ConfigureAwait(false);
            }

            if (!eventFlags.IsNormal())
            {
                await InvokeCacheListenerForRegionEventAsync(
                    callbackArgument, eventFlags, RegionEventType.AfterRegionClear, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _mutex.ExitReadLock();
        }
    }

    protected async ValueTask<bool> InvokeCacheWriterForRegionEventAsync(
        object? callbackArgument, CacheEventFlags eventFlags, RegionEventType type, CancellationToken ct = default)
    {
        var bCacheWriterReturn = true;
        if (_writer is not null)
        {
            var ev = new RegionEvent(this, callbackArgument, eventFlags.IsNotification());
            var eventStr = "unknown";
            try
            {
                var updateStats = true;
                var writerStart = Stopwatch.GetTimestamp();
                switch (type)
                {
                    case RegionEventType.BeforeRegionDestroy:
                        eventStr = "beforeRegionDestroy";
                        bCacheWriterReturn = await _writer.BeforeRegionDestroyAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case RegionEventType.BeforeRegionClear:
                        eventStr = "beforeRegionClear";
                        bCacheWriterReturn = await _writer.BeforeRegionClearAsync(ev, ct).ConfigureAwait(false);
                        break;
                    case RegionEventType.BeforeRegionInvalidate:
                    case RegionEventType.AfterRegionInvalidate:
                    case RegionEventType.AfterRegionDestroy:
                    case RegionEventType.AfterRegionClear:
                        updateStats = false;
                        break;
                }
                if (updateStats)
                {
                    _regionStats.WriterCall(Stopwatch.GetElapsedTime(writerStart));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex, "Exception in CacheWriter::{EventStr} on region {RegionPath}",
                    eventStr, FullPath);
                bCacheWriterReturn = false;
            }
        }
        return bCacheWriterReturn;
    }

    protected async ValueTask InvokeCacheListenerForRegionEventAsync(
        object? callbackArgument, CacheEventFlags eventFlags, RegionEventType type, CancellationToken ct = default)
    {
        if (_listener is null)
        {
            return;
        }

        var ev = new RegionEvent(this, callbackArgument, eventFlags.IsNotification());
        var eventStr = "unknown";
        try
        {
            var updateStats = true;
            var listenerStart = Stopwatch.GetTimestamp();
            switch (type)
            {
                case RegionEventType.AfterRegionDestroy:
                    eventStr = "afterRegionDestroy";
                    await _listener.AfterRegionDestroyAsync(ev, ct).ConfigureAwait(false);
                    _cachePerfStats.CacheListenerCallCompleted();
                    if (eventFlags.IsCacheClose())
                    {
                        eventStr = "close";
                        await _listener.CloseAsync(this, ct).ConfigureAwait(false);
                        _cachePerfStats.CacheListenerCallCompleted();
                    }
                    break;
                case RegionEventType.AfterRegionInvalidate:
                    eventStr = "afterRegionInvalidate";
                    await _listener.AfterRegionInvalidateAsync(ev, ct).ConfigureAwait(false);
                    _cachePerfStats.CacheListenerCallCompleted();
                    break;
                case RegionEventType.AfterRegionClear:
                    eventStr = "afterRegionClear";
                    await _listener.AfterRegionClearAsync(ev, ct).ConfigureAwait(false);
                    break;
                case RegionEventType.BeforeRegionInvalidate:
                case RegionEventType.BeforeRegionDestroy:
                case RegionEventType.BeforeRegionClear:
                    updateStats = false;
                    break;
            }
            if (updateStats)
            {
                _regionStats.ListenerCall(Stopwatch.GetElapsedTime(listenerStart));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex, "Exception in CacheListener::{EventStr} on region {RegionPath}",
                eventStr, FullPath);
            throw new CacheListenerException(
                $"CacheListener.{eventStr} failed on region '{FullPath}'.", ex);
        }
    }

    /// <inheritdoc />
    public override async Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key, nameof(key));
        await CheckDestroyPendingAsync(ct).ConfigureAwait(false);
        return ContainsKeyInternal(key);
    }

    /// <inheritdoc />
    public override Task<bool> ContainsKeyOnServerAsync(object key, CancellationToken ct = default) =>
        // cppcache LocalRegion::containsKeyOnServer (LocalRegion.cpp:671-675) throws
        //   UnsupportedOperationException; BCL substitution per use-bcl-exceptions
        //   memory rule. ThinClientRegion overrides with the real wire call.
        throw new NotSupportedException("LocalRegion.ContainsKeyOnServerAsync: not supported on a server-less region.");

    /// <inheritdoc />
    public override async Task DestroyAsync(object key, object? callbackArgument = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await DestroyNoThrowAsync(
            key, callbackArgument,
            updateCount: -1,
            eventFlags: CacheEventFlags.Normal,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ExistsValueAsync: pending OQL routing through ThinClientRegion override.");

    /// <inheritdoc />
    public override Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.GetAllAsync: pending local entry map.");

    /// <summary>
    /// Thin stat-timed wrapper. Mirrors cppcache <c>LocalRegion::get</c>
    /// (<c>cppcache/src/LocalRegion.cpp:340-354</c>) — times the op and
    /// delegates to <see cref="GetNoThrowAsync"/>. cppcache's
    /// <c>throwExceptionIfError</c> collapses: <see cref="GetNoThrowAsync"/>
    /// throws directly rather than returning a <c>GfErrType</c>.
    /// </summary>
    /// <remarks>
    ///   std::shared_ptr&lt;Cacheable&gt; LocalRegion::get(
    ///       const std::shared_ptr&lt;CacheableKey&gt;&amp; key,
    ///       const std::shared_ptr&lt;Serializable&gt;&amp; aCallbackArgument) {
    ///     std::shared_ptr&lt;Cacheable&gt; rptr;
    ///     int64_t sampleStartNanos = startStatOpTime();
    ///     GfErrType err = getNoThrow(key, rptr, aCallbackArgument);
    ///     updateStatOpTime(m_regionStats-&gt;getStat(), m_regionStats-&gt;getGetTimeId(),
    ///                      sampleStartNanos);
    ///     throwExceptionIfError("Region::get", err);
    ///     return rptr;
    ///   }
    /// </remarks>
    public override async Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sampleStartTimestamp = Stopwatch.GetTimestamp();
        try
        {
            return await GetNoThrowAsync(key, callback, ct).ConfigureAwait(false);
        }
        finally
        {
            // cppcache updateStatOpTime (LocalRegion.cpp:346) — record
            // regardless of success / failure, matching PutAsync.
            _regionStats.Get(Stopwatch.GetElapsedTime(sampleStartTimestamp));
        }
    }

    /// <inheritdoc />
    public override Task InvalidateAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.InvalidateAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.PutAllAsync: pending local entry map.");

    public override async Task PutAsync(object key, object? value, object? callbackArgument = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var sampleStartTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await PutNoThrowAsync(
                key, value, callbackArgument,
                updateCount: -1,
                eventFlags: CacheEventFlags.Normal,
                ct).ConfigureAwait(false);
        }
        finally
        {
            // cppcache updateStatOpTime (LocalRegion.cpp:364) — record
            // regardless of success / failure so the histogram counts
            // both outcomes.
            _regionStats.Put(Stopwatch.GetElapsedTime(sampleStartTimestamp));
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<object>> QueryAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.QueryAsync: pending OQL routing through ThinClientRegion override.");

    /// <inheritdoc />
    public override Task RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveAllAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> RemoveAsync(object key, object value, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveAsync (strict): pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> RemoveExAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveExAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.SelectValueAsync: pending OQL routing through ThinClientRegion override.");

    public override string FullPath { get; }

    /// <summary>
    /// Mirrors cppcache <c>LocalRegion::size()</c>
    /// (<c>cppcache/src/LocalRegion.cpp:619-629</c>): tx-aware dispatch
    /// over the local entry count. Renamed from cppcache's <c>size()</c>
    /// to <c>LocalCount</c> in the C# port — see <see cref="IRegion.LocalCount"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache differentiates the two paths via a non-virtual call
    /// qualifier (<c>LocalRegion::size_remote()</c> at line 628) for the
    /// no-tx branch vs. a virtual call (<c>size_remote()</c> at line
    /// 625) for the in-tx branch. C# has no syntax to bypass virtual
    /// dispatch from <c>this</c>, so the body is split into a non-virtual
    /// helper (<see cref="LocalSizeRemote"/>) plus a virtual hook
    /// (<see cref="SizeRemote"/>) — the no-tx branch calls the helper
    /// directly, the in-tx branch goes through the virtual.
    /// </para>
    /// <para>
    /// We deliberately do <b>not</b> mirror cppcache's
    /// <c>return GF_NOTSUP;</c> on the tx + isLocalOp case
    /// (<c>LocalRegion.cpp:623</c>) — that returns the raw int 12 as
    /// though it were an entry count, which looks like a cppcache bug.
    /// We throw <see cref="NotSupportedException"/> instead.
    /// </para>
    /// </remarks>
    public override int LocalCount
    {
        get
        {
            var txState = GetTXState();
            if (txState is not null)
            {
                if (IsLocalOp())
                {
                    // cppcache LocalRegion.cpp:622-624 returns GF_NOTSUP
                    // as a uint count — we throw instead. Pure local
                    // region can't satisfy a tx (no server to coordinate
                    // with), so calling LocalCount in this combo is API misuse.
                    throw new NotSupportedException(
                        "Region.LocalCount: not supported on a local-only region inside a transaction.");
                }
                return SizeRemote();
            }
            return LocalSizeRemote();
        }
    }

    public override string Name { get; }

    // ── Abstract RegionInternal members satisfied as NIE ───────
    // Mirrors cppcache LocalRegion being concrete: every IRegion op
    // has a "local default" sitting at this layer; ThinClientRegion
    // overrides them with wire-bound bodies. Until the local entry-map
    // (EntriesMap) ships we throw NotImplementedException — when
    // caching-enabled lands, these bodies switch to consulting
    // EntriesMap (cppcache LocalRegion.cpp:getNoThrow / putNoThrow
    // template path) and only fall through to a derived hook for the
    // network leg.

    /// <inheritdoc />
    public override IPool? Pool => null;

}
