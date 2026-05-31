using System.Collections.Concurrent;
using System.Xml.Linq;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Pdx;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Services;

internal sealed class GeodeCache(IServiceProvider serviceProvider) : IGeodeCache, IAsyncDisposable
{

    // Writer lives in the cppcache destroy path (CacheImpl::close /
    // CacheImpl::~CacheImpl), which we have not ported yet. Field is kept so
    // GetRegion mirrors cppcache 1:1 and the writer can land in place later.
#pragma warning disable CS0649
    private int _destroyPending;
#pragma warning restore CS0649
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Task? _initTask;
    readonly PoolManager _poolManager = serviceProvider.GetRequiredService<PoolManager>();
    // 存 RegionInternal(非 IRegion):cppcache `m_regions` 存 `shared_ptr<Region>`
    // 後 controller / internal helper 還要 `dynamic_pointer_cast<RegionInternal>` 一次;
    // C# 強型別不用付這個 cast — 進來的就一定是 RegionInternal subclass
    // (LocalRegion / ThinClientPoolRegion),把不變式抓進 compile-time。
    // 公開 GetRegion 仍 return IRegion?(implicit upcast),public API 不變。
    private readonly ConcurrentDictionary<string, RegionInternal> _regions = new(StringComparer.Ordinal);

    /// <summary>
    /// Heap-LRU coordinator,InitializeCoreAsync 內條件解析 + Start;CloseAsync 配對 StopAsync。
    /// null 表示本 cache 未啟用 heap-LRU(<see cref="SystemProperties.HeapLRULimitEnabled"/> 為 false),
    /// CloseAsync 用這個欄位判斷要不要 stop,避免無謂解析 DI 把鬼魂建出來。
    /// </summary>
    private EvictionController? _evictionController;
    readonly SerializationRegistry _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
    readonly SystemProperties _systemProperties = serviceProvider.GetRequiredService<SystemProperties>();
    readonly TcrConnectionManager _tcrConnectionManager = serviceProvider.GetRequiredService<TcrConnectionManager>();
    readonly TypedResultAdapter _typedResultAdapter = serviceProvider.GetRequiredService<TypedResultAdapter>();

    /// <summary>
    /// Build-time snapshot of the public <see cref="GeodeClientOptions"/>
    /// into the internal <see cref="SystemProperties"/> bag (cppcache
    /// "geode.properties → SystemProperties at cache build"). Subsequent
    /// mutations to the caller's <paramref name="opts"/> do NOT affect
    /// this cache.
    /// </summary>


    private async Task InitializeCoreAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        await _serializationRegistry.InitAsync(ct).ConfigureAwait(false);
        // ── 2. TCCM init ────────────────────────────────────────
        // Sets _isDurable from options.Subscription. In pool mode
        // (our MVP) the three background workers stay parked; this
        // is essentially a flag flip. Must complete before any pool
        // queries TCCM.IsDurable / haEnabled.
        await _tcrConnectionManager.InitAsync(isPool: true, ct).ConfigureAwait(false);

        // ── 3-5. Build and init pools ───────────────────────────
        // Both paths produce a sequence of CachePoolOptions; the
        // foreach below builds + inits each one uniformly. Multi-pool /
        // multi-server / locator gating now lives inside
        // ThinClientPoolDM's ctor, so Cache stays generic. Required-
        // field validation is the Options layer's job (Phase 1.1 收尾);
        // here we trust the input.
        //if (_options.Cache is null)
        //{
        //    // path (b) — Options-based (programmatic, the default).
        //    // TODO step 3.b: enumerate a yet-to-be-added programmatic
        //    //   pool-config surface (e.g. _options.Pools) and project
        //    //   into CachePoolOptions-shape items.
        //    throw new NotImplementedException(
        //        "TODO: Cache.InitializeCoreAsync step 3.b (path b — Options-based)");
        //}
        //else
        //{
        //    // path (a) — Declarative cache.xml-style. Mirrors cppcache
        //    // CacheImpl::initializeDeclarativeCache(xml).
        //    await InitializeDeclarativeCacheAsync(_options.Cache, ct).ConfigureAwait(false);
        //}

        // ── 6. EvictionController start (heap-LRU only) ────────
        // cppcache CacheImpl ctor (CacheImpl.cpp:84-88):
        //   if (prop.heapLRULimitEnabled()) {
        //     m_evictionController = make_unique<EvictionController>(...);
        //     m_evictionController->start();
        //     LOGINFO("Heap LRU eviction controller thread started");
        //   }
        // EC 是 DI Scoped — 解析一次後生命週期跟 cache scope 一樣。
        // 不解析就不會建出來(TryAddScoped 是 lazy),正好對應「heap-LRU 沒開
        // 就沒這個 service」的語意,避免建出一個 _maxHeapSize=0 的鬼魂。
        if (_systemProperties.HeapLRULimitEnabled)
        {
            _evictionController = serviceProvider.GetRequiredService<EvictionController>();
            _evictionController.Start();
        }

        // ── 7. PDX / serialization registration (Phase 2+) ──────
        // TODO: if (_options.Cache?.Pdx is { } pdx) apply pdx
        //   ignoreUnreadFields / readSerialized to _pdxTypeRegistry.
    }



    internal async Task InitializeAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        var task = Volatile.Read(ref _initTask);
        if (task is null)
        {
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                task = _initTask;
                if (task is null)
                {
                    task = InitializeCoreAsync(ct);
                    Volatile.Write(ref _initTask, task);
                }
            }
            finally
            {
                _initLock.Release();
            }
        }
        await task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Register a freshly-built region on the cache. Called by
    /// <see cref="RegionFactory.CreateAsync{TKey, TValue}(string, CancellationToken)"/>;
    /// mirrors cppcache <c>CacheImpl::createRegion</c> map insertion
    /// (<c>cppcache/src/CacheImpl.cpp:395-398, 440</c>).
    /// </summary>
    internal void RegisterRegion(string name, RegionInternal region)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (!_regions.TryAdd(name, region))
        {
            throw new RegionExistsException(
                $"CacheImpl::createRegion: \"{name}\" region exists in local cache");
        }
    }

    /// <summary>
    /// 4-way region kinds. Mirrors cppcache
    /// <c>CacheImpl::RegionKind</c> (<c>CacheImpl.hpp:334-339</c>).
    /// </summary>
    internal enum RegionKind
    {
        /// <summary>Local-only region, no server backing. cppcache <c>CPP_REGION</c>.</summary>
        Local,
        /// <summary>Non-pool legacy region (endpoints-based). cppcache <c>THINCLIENT_REGION</c>.</summary>
        ThinClient,
        /// <summary>Pool-backed with subscription / redundancy / durable. cppcache <c>THINCLIENT_HA_REGION</c>.</summary>
        ThinClientHA,
        /// <summary>Pool-backed (plain). cppcache <c>THINCLIENT_POOL_REGION</c>.</summary>
        ThinClientPool,
    }

    /// <summary>
    /// Pick the region kind from attributes. Mirrors cppcache
    /// <c>CacheImpl::getRegionKind</c>
    /// (<c>cppcache/src/CacheImpl.cpp:132-160</c>).
    /// </summary>
    internal RegionKind GetRegionKind(RegionAttributes attrs)
    {
        // TODO cppcache CacheImpl.cpp:137-146 — endpoints-based regions
        //   (THINCLIENT_REGION / "none" fallback). We have no endpoints
        //   field on RegionAttributes and per pool-only-no-non-pool memory
        //   the legacy path is structurally unreachable; left as a marker
        //   for the cppcache step.

        if (!string.IsNullOrEmpty(attrs.PoolName))
        {
            var pool = _poolManager.Find(attrs.PoolName);

            // TODO Phase 4+: cppcache CacheImpl.cpp:149-151 — HA kind
            //   when pool.SubscriptionRedundancy > 0 || pool.SubscriptionEnabled
            //   || TcrConnectionManager.IsDurable. ThinClientPoolDM
            //   doesn't expose those properties yet; default everyone to
            //   ThinClientPool until subscription / HA wiring lands.
            //   When this lands:
            //     if ((pool != null && (pool.SubscriptionRedundancy > 0
            //                           || pool.SubscriptionEnabled))
            //         || _tcrConnectionManager.IsDurable)
            //         return RegionKind.ThinClientHA;
            _ = pool;
            return RegionKind.ThinClientPool;
        }

        return RegionKind.Local;
    }

    /// <summary>
    /// Validate the region attributes against the cache state. Mirrors
    /// cppcache <c>CacheImpl::validateRegionAttributes</c>
    /// (declared at <c>CacheImpl.hpp:345-346</c>) plus the inline
    /// validations in <c>createRegion_internal</c>.
    /// </summary>
    private void ValidateRegionAttributes(string name, RegionAttributes attrs)
    {
        // TODO cppcache CacheImpl::validateRegionAttributes — port the
        //   full rule set. Today only the multi-user + caching combo
        //   below fires (cppcache puts it inside createRegion_internal;
        //   we lift it here so the rule is centralised).

        if (!string.IsNullOrEmpty(attrs.PoolName))
        {
            var pool = _poolManager.Find(attrs.PoolName);
            if (pool is not null /* TODO && !pool.IsDestroyed */)
            {
                // TODO Phase 3 multi-user: cppcache CacheImpl.cpp:532-541 —
                //   if pool.MultiuserAuthentication && attrs.CachingEnabled
                //   → throw IllegalStateException("Pool is in multiuser
                //   authentication so region local caching is not
                //   supported."). ThinClientPoolDM lacks the
                //   MultiuserAuthentication property; gate this when
                //   Phase 3 wires it.
                _ = pool;
            }
        }

        // TODO cppcache CacheImpl.cpp:545-553 — endpoints + poolName
        //   mutual exclusion. We have no endpoints field on
        //   RegionAttributes so the check is structurally redundant.
        _ = name;
    }

    /// <summary>
    /// Build the concrete region (4-way kind dispatch) and register it
    /// on the cache. Mirrors cppcache <c>CacheImpl::createRegion</c> +
    /// <c>CacheImpl::createRegion_internal</c>
    /// (<c>cppcache/src/CacheImpl.cpp:365-580</c>) — owns the RegionKind
    /// switch and orchestrates the full create-region pipeline.
    /// </summary>
    internal async Task<RegionInternal> CreateRegionAsync(string name, RegionAttributes attrs, CancellationToken ct = default)
    {
        // ── 1. First-time init block (cppcache CacheImpl.cpp:368-381)
        // TODO Phase 1.5: lock _initDoneLock, when !_initDone &&
        //   poolName.empty():
        //     m_tcrConnectionManager->init();
        //     m_remoteQueryServicePtr = make_shared<RemoteQueryService>(this);
        //     if (statisticsEnabled) m_adminRegion = AdminRegion::create(this);
        //   set _initDone = true. We drive init via InitializeAsync at
        //   cache build time today; the cppcache "lazy on first region
        //   create" path stays unported until non-pool regions arrive
        //   (which per pool-only-no-non-pool, they don't).

        // ── 2. throwIfClosed (cppcache CacheImpl.cpp:383)
        ObjectDisposedException.ThrowIf(IsClosed, this);

        // ── 3. Name validation (cppcache CacheImpl.cpp:385-388)
        // Already enforced at RegionFactory.CreateAsync; defensive
        // re-check so direct CacheImpl callers can't bypass it.
        if (name.Contains('/'))
        {
            throw new ArgumentException(
                "Malformed name string, contains region path seperator '/'",
                nameof(name));
        }

        // ── 4. Validate attrs (cppcache CacheImpl.cpp:390)
        ValidateRegionAttributes(name, attrs);

        // ── 5. Lock m_regions (cppcache CacheImpl.cpp:392-393)
        // ConcurrentDictionary handles the find+add race via TryAdd in
        // RegisterRegion below; the explicit lock is unnecessary in C#.

        // ── 6. Duplicate-name check (cppcache CacheImpl.cpp:395-398)
        // Done inside RegisterRegion at step 14 — TryAdd's bool return
        // is the atomic find-or-add we need.

        // ── 7. CacheStatistics (cppcache CacheImpl.cpp:400)
        // TODO Phase 2+: instantiate per-region CacheStatistics sink
        //   (lastModifiedTime / lastAccessedTime / hitCount / missCount
        //   / hitRatio) and thread through createRegion_internal. We
        //   have RegionStatistics (Meter) but that's the cppcache
        //   RegionStats analog, not CacheStatistics.

        // ── 8. createRegion_internal kind dispatch (cppcache
        //    CacheImpl.cpp:401-418, body at :520-581).
        RegionInternal region;
        try
        {
            region = await CreateRegionInternalAsync(name, parent: null, attrs, shared: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // TODO cppcache CacheImpl.cpp:410-417 — wrap unknown
            //   exceptions in UnknownException("...Failed to create
            //   Region ..."). .NET convention: propagate as-is; the
            //   stack trace + inner exception already preserve the cause.
            _ = ex;
            throw;
        }

        // ── 9. null check → RegionCreationFailedException (cppcache
        //    CacheImpl.cpp:420-423). C# `new` never returns null so the
        //    check is structurally impossible — left as a marker.

        // ── 10. addDisconnectedMessageToQueue (cppcache CacheImpl.cpp:426)
        // TODO Phase 4+: HA region after-creation hook — enqueue a
        //   synthetic disconnect event so listeners observe the gap
        //   from the moment the region exists. ThinClientHARegion-only.

        // ── 11. PersistenceManager init (cppcache CacheImpl.cpp:428-437)
        // CUT per CLAUDE.md «Not implemented» — disk overflow /
        //   persistence backup has no .NET counterpart we plan to port.

        // ── 12. acquireReadLock (cppcache CacheImpl.cpp:439)
        // TODO Phase 2+: LocalRegion.Mutex (boost::shared_mutex) reader
        //   lock; we have the field but no callers acquire it yet.

        // ── 13-14. Register region on cache (cppcache CacheImpl.cpp:440)
        RegisterRegion(name, region);

        // ── 15. PR single-hop metadata enqueue (cppcache CacheImpl.cpp:447-458)
        // TODO Phase 4+: when pool.PrSingleHopEnabled, enqueue the
        //   region's full-path with the pool's ClientMetadataService
        //   for initial single-hop metadata refresh.

        // ── 16. setRegionExpiryTask (cppcache CacheImpl.cpp:461)
        // TODO Phase 2+: schedule the root region's expiry task if
        //   region-level expiration is configured (RegionTimeToLive /
        //   RegionIdleTime on attrs).

        // ── 17. releaseReadLock (cppcache CacheImpl.cpp:462)
        // TODO Phase 2+: pair with step 12.

        return region;
    }

    /// <summary>
    /// 4-way region kind dispatch + concrete instantiation + InitTcrAsync
    /// per ThinClient subclass. Mirrors cppcache
    /// <c>CacheImpl::createRegion_internal</c>
    /// (<c>cppcache/src/CacheImpl.cpp:520-581</c>).
    /// </summary>
    private async Task<RegionInternal> CreateRegionInternalAsync(
        string name,
        RegionInternal? parent,
        RegionAttributes attrs,
        bool shared,
        CancellationToken ct)
    {
        // cppcache `bool shared` is the `enableTimeStatistics` flag in
        // disguise — OTel histograms are always on so the flag is moot;
        // accepted for ctor parity but ignored.
        _ = shared;

        var kind = GetRegionKind(attrs);

        switch (kind)
        {
            case RegionKind.ThinClient:
                // cppcache CacheImpl.cpp:555-561 — non-pool legacy.
                // Per pool-only-no-non-pool memory we don't run this
                // path; the case exists for cppcache parity so future
                // search for "THINCLIENT_REGION" lands here.
                throw new NotImplementedException(
                    "RegionKind.ThinClient (non-pool legacy) is structurally unreachable; see pool-only-no-non-pool memory.");

            case RegionKind.ThinClientHA:
                // cppcache CacheImpl.cpp:562-567.
                // TODO Phase 4+: subscription / HA wiring —
                //   ThinClientHARegion ctor takes (sp, name, parent,
                //   attrs, enableNotification). Once subscription /
                //   interest-list lands, instantiate via
                //   ActivatorUtilities and call InitTcrAsync (HA DM
                //   override).
                throw new NotImplementedException(
                    "RegionKind.ThinClientHA: subscription / HA wiring lands in Phase 4+.");

            case RegionKind.ThinClientPool:
            {
                // cppcache CacheImpl.cpp:568-574.
                var poolRegion = ThinClientPoolRegion.Create(serviceProvider, name, parent, attrs);

                // cppcache `tmp->initTCR()` after construction — pool
                // variant looks up the pool by name and attaches its
                // ThinClientPoolDM. See ThinClientPoolRegion.InitTcrAsync
                // override (currently inherited NIE from base).
                await poolRegion.InitTcrAsync(ct).ConfigureAwait(false);

                return poolRegion;
            }

            case RegionKind.Local:
            default:
                // cppcache CacheImpl.cpp:575-579 — no initTCR for LOCAL.
                return LocalRegion.Create(serviceProvider, name, parent, attrs);
        }
    }



    // ── Test / back-compat passthrough getters ───────────────────────
    //
    // Production code injects these via the DI scope directly (cache
    // doesn't proxy). These getters exist for tests that used the old
    // `cache.X` shape — they pull from the same scoped sp, so each
    // returns the same scoped instance as a fresh DI resolve would.
    // Safe to delete once tests migrate to scope resolution    

    //    /// <summary>
    //    /// Build pools and regions from an already-bound
    //    /// <see cref="CacheOptions"/> tree. Mirrors cppcache
    //    /// <c>CacheImpl::initializeDeclarativeCache(const std::string&amp;)</c>
    //    /// — the difference is we work off already-parsed options instead
    //    /// of running an XML parser (Xerces is bucket 1, cut per
    //    /// CLAUDE.md).
    //    /// </summary>
    //    /// <remarks>
    //    /// Two passes: pools first (so regions can resolve their pool
    //    /// references), then regions. Each pool's <c>InitAsync</c> opens
    //    /// real sockets — this is where I/O actually fires.
    //    /// </remarks>
    //    private async Task InitializeDeclarativeCacheAsync(CacheOptions cache, CancellationToken ct)
    //    {
    //        await InitializePoolsAsync(cache, ct).ConfigureAwait(false);

    //        // ── 6. Build regions ────────────────────────────────────
    //        // cppcache equivalent: CacheParser::create iterates
    //        // <region> elements and calls CacheImpl::createRegion(name,
    //        // attrs) for each top-level region (sub-regions handled
    //        // recursively in the parser itself).
    //        foreach (var xmlRegion in cache.Regions)
    //        {
    //            // Name structural validation (non-empty / non-whitespace)
    //            // and RefId existence are enforced by
    //            // GeodeClientOptionsValidator at host build time — no
    //            // inline checks needed here.

    //            // ── 6.1 Resolve refid template ─────────────────
    //            // cppcache CacheParser folds <region refid="..."> onto
    //            // a previously declared <region-attributes id="..."> at
    //            // parse time (CacheParser.cpp:777-786). We do the same
    //            // here: clone the template, then let xmlRegion.Attributes
    //            // override non-null / non-empty fields.
    //            var attributes = ResolveAttributes(xmlRegion, cache.NamedAttributes);

    //            // ── 6.2 Resolve pool ───────────────────────────
    //            // cppcache CacheImpl::createRegion_internal
    //            // (CacheImpl.cpp:524) looks up the pool by name; empty
    //            // PoolName falls through to PoolManager.DefaultPool
    //            // (Find("") returns DefaultPool).
    //            var pool = poolManager.Find(attributes.PoolName);
    //            if (pool is null)
    //            {
    //                // Either PoolName references a pool not declared in
    //                // Cache.Pools, or PoolName is empty and no pools
    //                // are registered (the validator should have caught
    //                // the second case; defensive guard).
    //                throw new InvalidOperationException(
    //                    $"Region '{xmlRegion.Name}' references pool " +
    //                    $"'{attributes.PoolName}' which is not registered " +
    //                    "(empty PoolName resolves to the default pool).");
    //            }

    //            // ── 6.3 IPool → ThinClientBaseDM ───────────────
    //            // MVP has only one IPool impl (ThinClientPoolDM, which
    //            // IS-A ThinClientBaseDM), so the cast is always safe
    //            // today. The pattern-match form gives a clearer error
    //            // message if a future non-DM IPool implementation
    //            // arrives (Phase 1.5+) than a raw InvalidCastException.
    //            if (pool is not ThinClientBaseDM dm)
    //            {
    //                throw new InvalidOperationException(
    //                    $"Pool '{attributes.PoolName}' " +
    //                    $"({pool.GetType().Name}) does not derive from " +
    //                    $"{nameof(ThinClientBaseDM)}; cannot be used as a " +
    //                    "region's distribution manager.");
    //            }

    //            // ── 6.4 Build ThinClientRegion ─────────────────
    //            // Phase 1.2 builds top-level regions only — `parent` is
    //            // always null until sub-region creation lands.
    //            // ActivatorUtilities can't match a null arg against the
    //            // `RegionInternal?` ctor slot (params object[] erases the
    //            // type), so resolve the logger from DI manually and call
    //            // the ctor directly. Mirrors what ActivatorUtilities would
    //            // have done minus the broken null-arg matching.
    //            var region = ActivatorUtilities.CreateInstance<ThinClientRegion>(
    //                serviceProvider,
    //                xmlRegion.Name,
    //                attributes,
    //                dm);

    //            // ── 6.5 Register ───────────────────────────────
    //            // cppcache CacheImpl::createRegion throws
    //            // RegionExistsException when m_regions already holds
    //            // the name. Future: GeodeClientOptionsValidator should
    //            // also flag duplicate names in Cache.Regions at
    //            // startup so this guard becomes pure belt-and-braces.
    //            if (!_regions.TryAdd(xmlRegion.Name, region))
    //            {
    //                throw new InvalidOperationException(
    //                    $"Region '{xmlRegion.Name}' is declared more than once " +
    //                    "in Cache.Regions.");
    //            }

    //            // ── 6.6 Sub-region children ────────────────────
    //            if (xmlRegion.ChildRegions.Count > 0)
    //            {
    //                // TODO: recurse into ChildRegions and build each as
    //                //   a sub-region of `region`. Mirrors cppcache
    //                //   CacheParser walking nested <region> elements
    //                //   and calling RegionInternal::createSubregion on
    //                //   the parent. Currently throws so XML-declared
    //                //   sub-regions aren't silently dropped.
    //                throw new NotImplementedException(
    //                    $"Region '{xmlRegion.Name}' declares " +
    //                    $"{xmlRegion.ChildRegions.Count} sub-region(s); " +
    //                    "sub-region creation is deferred to a later phase.");
    //            }
    //        }
    //    }

    //    /// <summary>
    //    /// Build and initialise every declared pool (or the synthesized
    //    /// default pool when <see cref="CacheOptions.Endpoints"/> is set
    //    /// instead). Real TCP / handshake fires inside each
    //    /// <c>InitAsync</c>.
    //    /// </summary>
    //    /// <remarks>
    //    /// cppcache <c>&lt;client-cache endpoints="..."&gt;</c> maps to
    //    /// <c>poolFactory_-&gt;addServer(...)</c>
    //    /// (<c>CacheXmlParser.cpp:553-560</c>). The validator guarantees
    //    /// <see cref="CacheOptions.Endpoints"/> and
    //    /// <see cref="CacheOptions.Pools"/> are mutually exclusive, so
    //    /// exactly one branch fires. The synthesized pool is built into
    //    /// a local list — we don't mutate the shared options instance,
    //    /// which would bleed across caches built from the same
    //    /// <c>IOptionsMonitor</c> snapshot.
    //    /// </remarks>
    //    private async Task InitializePoolsAsync(CacheOptions cache, CancellationToken ct)
    //    {
    //        foreach (var xmlPool in ResolvePoolsToBuild(cache))
    //        {
    //            // ctor enforces Phase 1.5 deferred limits (multi-server
    //            // / locator) internally; here we just hand it the pool
    //            // config and the shared TCCM. Positional args match
    //            // ThinClientPoolDM's primary ctor (xmlPool + options +
    //            // TCCM); ILogger is filled by DI.
    //            var pool = ActivatorUtilities.CreateInstance<ThinClientPoolDM>(
    //                serviceProvider, xmlPool, _options, tcrConnectionManager);
    //            poolManager.AddPool(xmlPool.Name, pool);

    //            // Pool.InitAsync internally:
    //            //   • locator query → endpoint list, OR direct server list
    //            //   • foreach endpoint → TcrEndpoint.CreateNewConnectionAsync(...)
    //            //       • socket open + handshake bytes
    //            //       • receive server-issued uniqueId
    //            //   • mark pool ready
    //            await pool.InitAsync(ct).ConfigureAwait(false);
    //        }
    //    }

    //    /// <summary>
    //    /// Apply a refid template (if any) and merge the region's inline
    //    /// attribute overrides on top. Mirrors cppcache
    //    /// <c>CacheParser</c> refid handling
    //    /// (<c>CacheParser.cpp:777-786</c>): non-empty
    //    /// <see cref="CacheRegionOptions.RefId"/> clones the named
    //    /// template; inline <see cref="CacheRegionOptions.Attributes"/>
    //    /// then overrides each field that is non-null (for value-type
    //    /// nullables) or non-empty (for plain strings).
    //    /// </summary>
    //    /// <remarks>
    //    /// <para>
    //    /// Chained refid is not honoured — a template's own
    //    /// <see cref="CacheRegionAttributesOptions.RefId"/> is ignored;
    //    /// templates must be self-contained.
    //    /// </para>
    //    /// <para>
    //    /// Returns <paramref name="xmlRegion"/>'s
    //    /// <see cref="CacheRegionOptions.Attributes"/> verbatim (same
    //    /// reference) when there is no <c>RefId</c> — no merge work, no
    //    /// allocation.
    //    /// </para>
    //    /// </remarks>
    //    private static CacheRegionAttributesOptions ResolveAttributes(
    //        CacheRegionOptions xmlRegion,
    //        IReadOnlyDictionary<string, CacheRegionAttributesOptions> namedAttributes)
    //    {
    //        if (string.IsNullOrEmpty(xmlRegion.RefId))
    //        {
    //            return xmlRegion.Attributes;
    //        }

    //        // Validator already enforces RefId membership; defensive guard
    //        // covers callers that bypass DI validation.
    //        if (!namedAttributes.TryGetValue(xmlRegion.RefId, out var template))
    //        {
    //            throw new InvalidOperationException(
    //                $"Region '{xmlRegion.Name}' RefId='{xmlRegion.RefId}' " +
    //                "does not match any key in Cache.NamedAttributes.");
    //        }

    //        var inline = xmlRegion.Attributes;
    //        return new CacheRegionAttributesOptions
    //        {
    //            // Nullable value types: inline non-null wins.
    //            CachingEnabled = inline.CachingEnabled ?? template.CachingEnabled,
    //            CloningEnabled = inline.CloningEnabled ?? template.CloningEnabled,
    //            Scope = inline.Scope ?? template.Scope,
    //            InitialCapacity = inline.InitialCapacity ?? template.InitialCapacity,
    //            LoadFactor = inline.LoadFactor ?? template.LoadFactor,
    //            ConcurrencyLevel = inline.ConcurrencyLevel ?? template.ConcurrencyLevel,
    //            LruEntriesLimit = inline.LruEntriesLimit ?? template.LruEntriesLimit,
    //            DiskPolicy = inline.DiskPolicy ?? template.DiskPolicy,
    //            ClientNotification = inline.ClientNotification ?? template.ClientNotification,
    //            ConcurrencyChecksEnabled = inline.ConcurrencyChecksEnabled ?? template.ConcurrencyChecksEnabled,

    //            // Plain strings: inline non-empty wins.
    //            Endpoints = string.IsNullOrEmpty(inline.Endpoints) ? template.Endpoints : inline.Endpoints,
    //            PoolName = string.IsNullOrEmpty(inline.PoolName) ? template.PoolName : inline.PoolName,

    //            // Inner RefId is not honoured (mirrors decision in
    //            // CacheRegionAttributesOptions doc); leave empty so the
    //            // resolved attributes don't accidentally trigger a second
    //            // round of resolution somewhere.
    //            RefId = string.Empty,

    //            // Reference types: inline non-null replaces wholesale (no deep merge).
    //            RegionTimeToLive = inline.RegionTimeToLive ?? template.RegionTimeToLive,
    //            RegionIdleTime = inline.RegionIdleTime ?? template.RegionIdleTime,
    //            EntryTimeToLive = inline.EntryTimeToLive ?? template.EntryTimeToLive,
    //            EntryIdleTime = inline.EntryIdleTime ?? template.EntryIdleTime,
    //            PartitionResolver = inline.PartitionResolver ?? template.PartitionResolver,
    //            CacheLoader = inline.CacheLoader ?? template.CacheLoader,
    //            CacheListener = inline.CacheListener ?? template.CacheListener,
    //            CacheWriter = inline.CacheWriter ?? template.CacheWriter,
    //            PersistenceManager = inline.PersistenceManager ?? template.PersistenceManager,
    //        };
    //    }

    //    /// <summary>
    //    /// Pure projection from <see cref="CacheOptions"/> to the list of
    //    /// pools the cache should build. When <see cref="CacheOptions.Endpoints"/>
    //    /// is non-empty, synthesises a single <c>"default"</c>-named
    //    /// <see cref="CachePoolOptions"/> whose <see cref="CachePoolOptions.Servers"/>
    //    /// is a deep copy of the endpoint list; otherwise returns
    //    /// <see cref="CacheOptions.Pools"/> as-is. Validator guarantees
    //    /// the two are mutually exclusive.
    //    /// </summary>
    //    internal static IReadOnlyList<CachePoolOptions> ResolvePoolsToBuild(CacheOptions cache)
    //    {
    //        if (cache.Endpoints.Count == 0) return cache.Pools;

    //        return
    //        [
    //            new()
    //            {
    //                Name = "default",
    //                Servers = cache.Endpoints.Select(e => e.Clone()).ToList(),
    //            },
    //        ];
    //    }

    ///// <summary>
    ///// Test-only escape hatch: expose the scoped <see cref="PoolManager"/>
    ///// so integration tests can reach <see cref="ThinClientPoolDM"/>
    ///// internals (e.g. <c>PoolSize</c>) without DI scope wrangling. Not
    ///// part of the public API — gated by <c>InternalsVisibleTo</c>.
    ///// </summary>
    //internal PoolManager PoolManager => poolManager;

    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (IsClosed) return;   // idempotent

        // Mirror cppcache CacheImpl::close() ordering:
        //   TODO Phase 1.5: TCCM.CloseAsync — stop background workers
        //     (m_tcrConnectionManager->close() comes first in cppcache so
        //     scheduled ping tasks can't fire on torn-down state).
        //   ✓ Regions release(下面 snapshot + dispose 迴圈)
        //   ✓ Pool close
        //   ✓ IsClosed flip(最末,對齊 cppcache m_closed)

        // ── Regions dispose ──────────────────────────────────────
        // Snapshot 取出 + 立刻 Clear,避免 dispose 迴圈跑到一半有人
        // 透過 Register/GetRegion 看到「半生不熟」的狀態。Dispose 本身
        // 是 idempotent(LocalRegion.DisposeAsync 用 _released guard),
        // 所以 DI scope 後續 teardown 再叫一次也安全。
        //
        // 每個 region dispose 失敗不該打斷 cache teardown — try/catch
        // 圈起來逐個釋放,把錯誤吞掉繼續(TODO: 加 ILogger<GeodeCache>
        // 後改成 LogWarning,目前先安靜處理避免噪音)。
        //
        // 順序考量:在 pool drain 之前 dispose,確保 region 拆解時
        // cache-scoped DI services(EvictionController / PoolManager /
        // TcrConnectionManager)還活著 — LRUEntriesMap.DisposeAsync 會
        // 走到 EvictionController.UnregisterRegion,EC 此時還在 scope 內。
        var regionsToDispose = _regions.Values.ToList();
        _regions.Clear();
        foreach (var region in regionsToDispose)
        {
            try
            {
                await region.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // TODO: structured log once ILogger<GeodeCache> is injected.
            }
        }

        // ── EvictionController stop(heap-LRU only)──────────────
        // Regions 全 dispose 完才 stop — 確保所有 LRUEntriesMap.DisposeAsync
        // 已經跑過 UnregisterRegion / IncrementHeapSize(-_currentMapSize),
        // EC 名單清空後再關 thread。null = 本 cache 沒啟用 heap-LRU,跳過。
        if (_evictionController is not null)
        {
            await _evictionController.StopAsync().ConfigureAwait(false);
        }

        // ── Pool drain ───────────────────────────────────────────
        // 把 pool.DestroyAsync cascade 進每個 ThinClientPoolDM(取消
        // conn-management loop、釋放 timers、drain connections)。
        // PoolManager.CloseAsync 自身 idempotent,DI scope 之後 dispose 安全。
        await _poolManager.CloseAsync(keepAlive: false, ct).ConfigureAwait(false);
        IsClosed = true;
    }

    /// <summary>
    /// Mirrors cppcache <c>Cache::createRegionFactory(RegionShortcut)</c>
    /// (<c>cppcache/src/Cache.cpp</c> → <c>CacheImpl::createRegionFactory</c>).
    /// Direct <see langword="new"/> rather than ActivatorUtilities — we
    /// already hold every ctor arg, and the cache instance is the natural
    /// back-pointer for the factory's eventual register-on-create step.
    /// </summary>
    public IRegionFactory CreateRegionFactory(RegionShortcut shortcut)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return ActivatorUtilities.CreateInstance<RegionFactory>(serviceProvider, shortcut);
    }

    public async ValueTask DisposeAsync()
    {
        // Forward to CloseAsync; idempotent until connection logic lands.
        await CloseAsync().ConfigureAwait(false);

        // TCCM is now DI-Scoped — the per-cache AsyncServiceScope
        // disposes it for us in reverse-resolve order, after Cache.
        // PoolManager / ClientProxyMembershipIdBuilder / CacheScopeContext
        // ride the same cascade.

        _initLock.Dispose();
    }

    /// <summary>
    /// Shared typed-result adapter used to wrap freshly-created regions
    /// in <see cref="RegionView{TKey, TValue}"/>; same instance the
    /// cache hands to <see cref="GetRegion{TKey, TValue}(string)"/>.
    /// </summary>
    //internal Protocol.Serialization.TypedResultAdapter TypedResultAdapter => _typedResultAdapter;

    /// <summary>
    /// Delegates to <c>PoolManager.DefaultPool.QueryService</c> (or the
    /// named pool's). Mirrors cppcache <c>CacheImpl::getQueryService()</c>
    /// pool-mode branch (<c>CacheImpl.cpp:171-203</c>); the non-pool
    /// fallback in the same method has no .NET counterpart per memory
    /// <c>pool-only-no-non-pool.md</c>.
    /// </summary>
    public IQueryService GetQueryService(string? poolName = null)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);

        // null / empty → DefaultPool. Aligns with PoolManager.Find's
        // own empty-string convention, but null gets normalised here
        // so PoolManager.Find (which throws on null) never sees it.
        if (string.IsNullOrEmpty(poolName))
        {
            var defaultPool = _poolManager.DefaultPool
                ?? throw new InvalidOperationException(
                    "Cache has no default pool — call EnsureInitializedAsync " +
                    "first or ensure at least one pool is registered.");
            return defaultPool.QueryService;
        }

        var pool = _poolManager.Find(poolName)
            ?? throw new ArgumentException(
                $"Pool '{poolName}' is not registered.", nameof(poolName));
        return pool.QueryService;
    }

    /// <summary>
    /// Mirrors cppcache <c>CacheImpl::getRegion</c>
    /// (<c>cppcache/src/CacheImpl.cpp:475-518</c>) line-for-line:
    /// throwIfClosed, m_destroyPending check (returns null), path
    /// validation, leading-slash strip, first-segment lookup,
    /// sub-region recursion via <c>region-&gt;getSubregion(remainder)</c>.
    /// </summary>
    public IRegion? GetRegion(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // cppcache: throwIfClosed
        ObjectDisposedException.ThrowIf(IsClosed, this);

        // cppcache lock_guard(m_destroyCacheMutex) is unnecessary —
        // ConcurrentDictionary covers map-side races, and
        // _destroyPending is a single atomic int.
        if (Volatile.Read(ref _destroyPending) != 0)
        {
            // cppcache CacheImpl.cpp:483 — silent null when destroy is
            // mid-flight, distinct from throwIfClosed (which fires
            // after IsClosed flips true).
            return null;
        }

        // cppcache: path == "/" || path.length() < 1 →
        //   IllegalArgumentException("Cache::getRegion: path is empty
        //   or a /"). We split into ArgumentException for empty (BCL
        //   ArgumentException.ThrowIfNullOrEmpty) and for "/".
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (path == "/")
        {
            throw new ArgumentException("Cache.GetRegion: path is empty or '/'.", nameof(path));
        }

        // cppcache: strip a single leading "/".
        var fullname = path.StartsWith('/') ? path[1..] : path;

        // cppcache: split at first '/'; left segment is the root region
        // name, the rest (if any) is the sub-region path.
        var idx = fullname.IndexOf('/');
        var stepname = idx < 0 ? fullname : fullname[..idx];

        // cppcache findRegion(stepname): pure map lookup.
        if (!_regions.TryGetValue(stepname, out var region))
        {
            return null;
        }

        if (idx >= 0)
        {
            // cppcache CacheImpl.cpp:504 — recurse into sub-region tree.
            //   var remainder = fullname[(idx + 1)..];
            //   region = region.GetSubregion(remainder);
            // TODO sub-region phase: IRegion has no GetSubregion yet;
            //   add it once the sub-region API surfaces. Until then,
            //   any path with an interior '/' falls through to NIE so
            //   callers don't silently get the root when they asked
            //   for a child.
            throw new NotImplementedException(
                $"Sub-region path '{path}' not yet supported; sub-region " +
                "API lands in a future phase.");
        }

        // TODO Phase 3 multi-user: cppcache CacheImpl.cpp:509-514 —
        //   if (isPoolInMultiuserMode(*region)) LOGWARN("...attached
        //   with region ... is in multiuser authentication mode...").

        return region;
    }

    public IRegion<TKey, TValue>? GetRegion<TKey, TValue>(string path)
    where TKey : IEquatable<TKey>
    {
        // Untyped lookup does the cppcache-faithful work (path validation,
        // sub-region recursion, destroyPending check). RegionView is a
        // pure compile-time wrapper — TKey/TValue are not runtime-bound.
        var region = GetRegion(path);
        return region is null ? null : new RegionView<TKey, TValue>(region, _typedResultAdapter);
    }



    public bool IsClosed { get; private set; }

    public string Name => _systemProperties.Name;

    public IPoolManager PoolManager => _poolManager;


    //    public ITypeRegistry TypeRegistry { get; } = typeRegistry;

    //    public bool PdxIgnoreUnreadFields => _options.Cache?.Pdx.IgnoreUnreadFields ?? false;
    //    public bool PdxReadSerialized => _options.Cache?.Pdx.ReadSerialized ?? false;


    //#pragma warning disable CS0169, CS0414, CS0649 // placeholder fields mirroring CacheImpl; wired up phase by phase

    //    // ── Lifecycle (CacheImpl.hpp:359-374) ──
    //    // m_closed       → IsClosed property (already exposed)
    //    // m_initialized  → captured by _initTask (null = not started)
    //    // m_initDoneLock → _initLock (SemaphoreSlim, async-friendly)
    //    // m_destroyCacheMutex → bucket 1, replaced by System.Threading.Lock

    //    private bool _keepAlive;              // m_keepAlive



    //    // ── Connection / Pool (CacheImpl.hpp:330, 362-363, 369) ──
    //    private object? _distributedSystem;   // m_distributedSystem
    //    // m_tcrConnectionManager / m_poolManager / m_clientProxyMembershipIDFactory
    //    //                                  → fields above (DI / Cache-owned)

    //    // ── Query (CacheImpl.hpp:370) ──
    //    // cppcache m_remoteQueryServicePtr is the non-pool fallback —
    //    // CacheImpl owns its own RemoteQueryService when no default pool
    //    // exists. We are pool-only (memory pool-only-no-non-pool.md), so
    //    // GetQueryService always delegates to PoolManager and never builds
    //    // a cache-owned service. The cppcache field has no .NET counterpart.

    //    // ── Transactions (CacheImpl.hpp:376) ──
    //    private object? _cacheTransactionManager; // m_cacheTXManager

    //    // ── PDX / serialization (CacheImpl.hpp:323-324, 379-383) ──
    //    private bool _pdxIgnoreUnreadFields;  // m_ignorePdxUnreadFields
    //    private bool _pdxReadSerialized;      // m_readPdxSerialized
    //    private object? _pdxTypeRegistry;     // m_pdxTypeRegistry
    //    private object? _serializationRegistry;// m_serializationRegistry

    //    // ── Versioning (CacheImpl.hpp:378) ──
    //    private object? _memberListForVersionStamp; // m_memberListForVersionStamp

    //    // ── Partition-routing flags (CacheImpl.hpp:320-322) ──
    //    private int _networkHop;              // m_networkhop (Interlocked 0/1)
    //    private int _prMetadataUpdated;       // m_pr_metadata_updated (Interlocked 0/1)
    //    private int _serverGroupFlag;         // m_serverGroupFlag (Interlocked int8_t)

    //    // ── Auth (CacheImpl.hpp:382) ──
    //    private object? _authInitialize;      // m_authInitialize

    //#pragma warning restore CS0169, CS0414, CS0649


}
