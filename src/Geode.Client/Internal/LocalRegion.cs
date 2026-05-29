using System.Diagnostics;
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

    static ObjectFactory<LocalRegion> _objectFactory
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
    protected IPool? AttachedPool;

    /// <summary>cppcache <c>m_cacheStatistics</c>: per-region access / modification timestamps.</summary>
    protected CachePerfStatistics CacheStatistics;

    /// <summary>cppcache <c>m_destroyPending</c>: region teardown in progress.</summary>
    protected bool DestroyPending;

    /// <summary>cppcache <c>m_enableTimeStatistics</c>: time-histogram flag (OTel always on; kept for parity).</summary>
    protected bool EnableTimeStatistics;

    /// <summary>cppcache <c>expiry_task_id_</c>: region-level ExpiryTask id.</summary>
    protected object? ExpiryTaskId;

    /// <summary>cppcache <c>m_isPRSingleHopEnabled</c>: single-hop routing for partitioned regions (Phase 4).</summary>
    protected bool IsPrSingleHopEnabled;

    /// <summary>cppcache <c>m_listener</c>: CacheListener (Phase 2+).</summary>
    protected object? Listener;

    /// <summary>cppcache <c>m_loader</c>: CacheLoader (Phase 2+).</summary>
    protected object? Loader;

    /// <summary>cppcache <c>m_entries</c> (<c>EntriesMap*</c>): local entry map (Phase 2+ caching-enabled). Renamed from cppcache's <c>m_entries</c> to (a) avoid clash with <see cref="IRegion.Entries(bool)"/> and (b) separate from the <see cref="EntriesMap"/> type name.</summary>
    protected Lazy<EntriesMap?> LocalEntriesMap;

    /// <summary>
    /// cppcache <c>mutex_</c>: region-wide reader-writer lock
    /// (cppcache 用 <c>boost::shared_mutex</c>;C# 港用
    /// <see cref="AsyncReaderWriterLock"/> 的 Phase 1.x 降級實作 —
    /// 排他鎖偽裝成 RW lock,API shape 對齊未來真 RW 實作)。
    /// </summary>
    protected readonly AsyncReaderWriterLock Mutex = new();

    /// <summary>cppcache <c>m_persistenceManager</c>: PersistenceManager (CLAUDE.md «Not implemented»).</summary>
    protected object? PersistenceManager;

    /// <summary>
    /// cppcache <c>m_regionStats</c>: per-region Meter sink.
    /// </summary>
    protected readonly RegionStatistics RegionStats;

    /// <summary>cppcache <c>m_released</c>: dispose path completed.</summary>
    protected bool Released;

    // ── cppcache LocalRegion fields (LocalRegion.hpp:511-530, 564) ─
    // Mirror every cppcache LocalRegion member here so the surface
    // matches 1:1. Types we have a C# port for use the real type
    // (RegionStatistics / IPool); everything else parks as `object?`
    // until the feature ships. Bool flags carry their cppcache default.
    // None of these are read today — they exist so future feature
    // ports (caching, expiry, listener, tombstone, single-hop, …)
    // land as body fills against the already-present field set.

    /// <summary>cppcache <c>m_subRegions</c>: synchronized_map name → sub-region.</summary>
    protected object? SubRegions;

    /// <summary>cppcache <c>m_tombstoneList</c>: CRDT tombstone tracking. Phase 2+ concurrency-checks 落地時 allocate。</summary>
    internal TombstoneList? TombstoneList;

    /// <summary>cppcache <c>m_transactionEnabled</c>: TX support flag.</summary>
    protected bool TransactionEnabled;

    /// <summary>cppcache <c>m_writer</c>: CacheWriter (Phase 2+).</summary>
    protected object? Writer;

    public LocalRegion(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes) : base(attributes)
    {
        Parent = parent;
        FullPath = parent is null ? "/" + name : parent.FullPath + "/" + name;
        Name = name;
        LocalEntriesMap = new Lazy<EntriesMap?>(() =>
        {
            if (attributes.CachingEnabled)
            {
                return EntriesMapFactory.CreateMap(serviceProvider, this, attributes);
            }
            return null;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        RegionStats = ActivatorUtilities.CreateInstance<RegionStatistics>(serviceProvider, FullPath);
        _logger = serviceProvider.GetRequiredService<ILogger<LocalRegion>>();
        CacheStatistics = serviceProvider.GetRequiredService<CachePerfStatistics>();
        _serviceProvider = serviceProvider;
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
            return LocalEntriesMap.Value!.Count;
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
        if (Writer is not null && action.EventFlags.InvokeCacheWriter())
        {
            action.GetCallbackOldValue();
            // invokeCacheWriterForEntryEvent method has the check that if oldValue
            // is a CacheableToken then it sets it to nullptr; also determines if it
            // should be BEFORE_UPDATE or BEFORE_CREATE depending on oldValue
            if (!InvokeCacheWriterForEntryEvent(action.Key, action.OldValue, action.Value,
                action.CallbackArgument, action.EventFlags, action.BeforeEventType))
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
                if ((action.UpdateCount = LocalEntriesMap.Value!.AddTrackerForEntry(action.Key, action.OldValue, action.AddIfAbsent, action.FailIfPresent, true)) < 0)
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
                    LocalEntriesMap.Value!.RemoveTrackerForEntry(action.Key);
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
                            CacheStatistics.DeltaMessageFailure();

                            // Get full object from server.
                            var (newValue1, versionTag1) = await GetNoThrowFullObjectAsync(null, ct);
                            if (newValue1 is not null)
                            {
                                try
                                {
                                    (action.Entry, action.OldValue, _) = LocalEntriesMap.Value!.Put(action.Key, newValue1, action.UpdateCount, 0,
                                        versionTag1 ?? action.VersionTag!);
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
                LocalEntriesMap.Value!.RemoveTrackerForEntry(action.Key);
            }
        }
        if (!action.EventFlags.IsNoCallbacks())
        {
            await InvokeCacheListenerForEntryEvent(action.Key, action.OldValue, action.Value,
                action.CallbackArgument, action.EventFlags, action.AfterEventType, ct);
        }
    }

    /// <summary>
    /// Throws <see cref="RegionDestroyedException"/> when the region's
    /// lifecycle flag (<see cref="DestroyPending"/>) is set. Mirrors
    /// cppcache <c>CHECK_DESTROY_PENDING_NOTHROW</c> macro
    /// (<c>cppcache/src/LocalRegion.hpp:66-74</c>) — collapsed into a
    /// method since C# has no macros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache macro does two things: (1) take a region-wide
    /// <c>shared_lock</c> on <c>mutex_</c>, (2) read
    /// <c>m_destroyPending</c> + return <c>GF_CACHE_REGION_DESTROYED_EXCEPTION</c>
    /// on truth. C# port: (1) <see cref="Mutex"/> read-lock via
    /// <see cref="AsyncReaderWriterLock.EnterReadLockAsync"/> (Phase
    /// 1.x 降級成排他鎖,Phase 2+ 換真 RW 實作不動 call site);
    /// (2) <c>throw</c> 取代 err-code,同
    /// <see cref="IRegionAction.CheckArgs"/> 的 exception-only 策略。
    /// </para>
    /// <para>
    /// cppcache's sibling <c>CHECK_DESTROY_PENDING</c> (throwing
    /// variant; <c>LocalRegion.hpp:54-64</c>) collapses to the same
    /// method here — the <c>NoThrow</c> / non-<c>NoThrow</c> split is
    /// the cppcache err-code-vs-exception divide and disappears under
    /// our exception-only design.
    /// </para>
    /// </remarks>
    protected async Task CheckDestroyPendingAsync(CancellationToken ct = default)
    {
        //#define CHECK_DESTROY_PENDING_NOTHROW(lock_type)         \
        //        boost::lock_type < decltype(mutex_) > checkGuard{ mutex_}; \
        //  do  {                                                   \
        //    if (m_destroyPending){    \
        //      return GF_CACHE_REGION_DESTROYED_EXCEPTION;        \
        //    }                                                    \
        //  } while (0)

        await Mutex.EnterReadLockAsync(ct).ConfigureAwait(false);
        try
        {
            if (DestroyPending)
            {
                throw new RegionDestroyedException(
                    $"Region {FullPath} has been destroyed.");
            }
        }
        finally
        {
            Mutex.ExitReadLock();
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

    /// <summary>
    /// CacheListener dispatch — 派發到 <c>Listener</c> 的
    /// <c>AfterCreate</c> / <c>AfterUpdate</c> / <c>AfterDestroy</c> /
    /// <c>AfterInvalidate</c> callback。Mirrors cppcache
    /// <c>LocalRegion::invokeCacheListenerForEntryEvent</c>
    /// (<c>cppcache/src/LocalRegion.hpp:546</c>).
    /// </summary>
    /// <remarks>
    /// Phase 2+ CacheListener feature 落地才填 body。Phase 1.x
    /// <see cref="Listener"/> 永遠 <see langword="null"/>,call site
    /// 在 <c>UpdateNoThrowAsync</c> 不被 <see cref="CacheEventFlagsExtensions.IsNoCallbacks"/>
    /// 擋住的話會走到這 — 之後 body 內部要先看 <c>Listener is null</c>
    /// 直接 return。NIE stub 純占位讓翻譯行對得起來。
    /// </remarks>
    protected Task InvokeCacheListenerForEntryEvent(
        object key,
        object? oldValue,
        object? newValue,
        object? aCallbackArgument,
        CacheEventFlags eventFlags,
        EntryEventType type,
        CancellationToken ct) =>
        throw new NotImplementedException(
            "LocalRegion.InvokeCacheListenerForEntryEvent: pending Phase 2+ CacheListener feature.");

    /// <summary>
    /// CacheWriter dispatch — 依 <paramref name="type"/> 派發到
    /// <c>Writer</c> 的 <c>BeforeCreate</c> / <c>BeforeUpdate</c> /
    /// <c>BeforeDestroy</c> / <c>BeforeInvalidate</c> callback;回
    /// <see langword="true"/> 表 writer 同意該 op,<see langword="false"/>
    /// 表 veto。Mirrors cppcache
    /// <c>LocalRegion::invokeCacheWriterForEntryEvent</c>
    /// (<c>cppcache/src/LocalRegion.cpp:2573-2660</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache 的 <c>BEFORE_UPDATE</c> 分支若 <paramref name="oldValue"/>
    /// 為 <see langword="null"/> 會 fall through 到 <c>BEFORE_CREATE</c>
    /// — 行為 1:1 翻過來時要保留。
    /// </para>
    /// <para>
    /// Phase 2+ CacheWriter feature 落地才填 body。Phase 1.x
    /// <see cref="Writer"/> 永遠 <see langword="null"/>,call sites
    /// 一律被 <c>if (Writer is not null &amp;&amp; ...)</c> 擋住,這個
    /// method 不會被觸發 — NIE stub 純占位讓翻譯行對得起來。
    /// </para>
    /// </remarks>
    protected bool InvokeCacheWriterForEntryEvent(
        object key,
        object? oldValue,
        object? newValue,
        object? aCallbackArgument,
        CacheEventFlags eventFlags,
        EntryEventType type) =>
        throw new NotImplementedException(
            "LocalRegion.InvokeCacheWriterForEntryEvent: pending Phase 2+ CacheWriter feature.");

    /// <summary>
    /// Schedule the entry-level expiry task for a freshly-created (or
    /// freshly-promoted) entry. Mirrors cppcache
    /// <c>LocalRegion::registerEntryExpiryTask</c>
    /// (<c>cppcache/src/LocalRegion.hpp:558</c>).
    /// </summary>
    /// <remarks>
    /// Phase 2+ expiry plumbing: cppcache pushes an <c>ExpiryTask</c>
    /// onto <see cref="ExpiryTaskId"/>'s scheduler. NIE placeholder
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
    protected virtual void UpdateAccessAndModifiedTime(bool modified) =>
        throw new NotImplementedException(
            "LocalRegion.UpdateAccessAndModifiedTime: pending Phase 2+ expiry plumbing.");

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
    protected virtual void UpdateAccessAndModifiedTimeForEntry(MapEntry entry, bool modified) =>
        throw new NotImplementedException(
            "LocalRegion.UpdateAccessAndModifiedTimeForEntry: pending Phase 2+ expiry plumbing.");

    /// <summary>
    /// Whether the region's entry-level expiry policy is configured (TTL
    /// or idle-timeout &gt; 0). Mirrors cppcache
    /// <c>RegionInternal::entryExpiryEnabled</c>
    /// (<c>cppcache/src/RegionInternal.hpp:313-315</c>) — inline
    /// non-virtual pure-getter over <c>m_regionAttributes</c>. Property
    /// in C# port (data-like, no side effects).
    /// </summary>
    /// <remarks>
    /// Phase 2+ expiry feature: real body reads
    /// <c>Attributes.EntryTimeToLive</c> / <c>EntryIdleTimeout</c>; NIE
    /// placeholder until those config knobs land.
    /// </remarks>
    protected bool EntryExpiryEnabled => throw new NotImplementedException(
        "LocalRegion.EntryExpiryEnabled: pending Phase 2+ entry-expiry config (TTL / idle-timeout).");

    /// <summary>
    /// Parent region in the sub-region tree, or <see langword="null"/> for a
    /// root region. Mirrors cppcache <c>LocalRegion::m_parentRegion</c>.
    /// </summary>
    protected RegionInternal? Parent { get; }

    internal static LocalRegion Create(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes)
    {
        return _objectFactory(serviceProvider, [name, parent, attributes]);
    }

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
    /// Phase 2+ caching-enabled: needs <see cref="LocalEntriesMap"/>
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
                (entry, oldValue) = LocalEntriesMap.Value!.Create(key, value!, updateCount, destroyTracker, versionTag);
            }
            else
            {
                try
                {
                    (entry, oldValue, isUpdate) = LocalEntriesMap.Value!.Put(key, value!, updateCount, destroyTracker, versionTag!, delta);
                }
                catch (GfErrTypeException ex) when (ex.Code == GfErrType.InvalidDelta)
                {
                    CacheStatistics.DeltaMessageFailure();
                    var (newValue1, versionTag1) = await GetNoThrowFullObjectAsync(eventId, ct)
                        .ConfigureAwait(false);
                    if (newValue1 is not null)
                    {
                        (entry, oldValue, isUpdate) = LocalEntriesMap.Value!.Put(key, newValue1, updateCount, destroyTracker, (versionTag1 ?? versionTag)!);
                    }
                }
                // Means that delta is on and there is no failure.
                if (delta is not null)
                {
                    CacheStatistics.DeltaReceived();
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
            CacheStatistics.Put();
        }
        else
        {
            RegionStats.Create();
            CacheStatistics.Create();
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



    /// <inheritdoc />
    public override Task ClearAsync(object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ClearAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ContainsKeyAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ExistsValueAsync: pending OQL routing through ThinClientRegion override.");

    /// <inheritdoc />
    public override Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.GetAllAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.GetAsync: pending local entry map.");

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
            RegionStats.Put(Stopwatch.GetElapsedTime(sampleStartTimestamp));
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
    public override IPool Pool =>
        throw new NotImplementedException("LocalRegion has no attached pool; ThinClientRegion override carries it.");

    

}
