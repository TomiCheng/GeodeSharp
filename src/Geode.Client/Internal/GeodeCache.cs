using System.Collections.Concurrent;
using Geode.Client.Options;
using Geode.Client.Pdx;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

internal sealed class GeodeCache : IGeodeCache, IAsyncDisposable
{
    private int _destroyPending;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Task? _initTask;
    private readonly string _name;
    private readonly IServiceProvider _serviceProvider;
    private readonly Lazy<PoolManager> _poolManager;
    private readonly ConcurrentDictionary<string, IRegion> _regions = new(StringComparer.Ordinal);
    private readonly SystemProperties _systemProperties;
    private readonly Lazy<TcrConnectionManager> _tcrConnectionManager;
    private readonly TypedResultAdapter _typedResultAdapter;
    private readonly TypeRegistry _typeRegistry;
    private readonly PdxTypeRegistry _pdxTypeRegistry;
    private readonly Lazy<SerializationRegistry> _serializationRegistry;
    private readonly EventIdGenerator _eventIdGenerator = new();
    public GeodeCache(IServiceProvider serviceProvider, string name, GeodeClientOptions? options = null)
    {
        _name = name;
        _serviceProvider = serviceProvider;
        _systemProperties = BuildSystemProperties(options);
        _typedResultAdapter = ActivatorUtilities.CreateInstance<TypedResultAdapter>(serviceProvider);
        _typeRegistry = ActivatorUtilities.CreateInstance<TypeRegistry>(serviceProvider);
        _pdxTypeRegistry = ActivatorUtilities.CreateInstance<PdxTypeRegistry>(serviceProvider);
        _poolManager = new Lazy<PoolManager>(
                    () => ActivatorUtilities.CreateInstance<PoolManager>(serviceProvider, this),
                    LazyThreadSafetyMode.ExecutionAndPublication);
        _tcrConnectionManager = new Lazy<TcrConnectionManager>(
                   () => ActivatorUtilities.CreateInstance<TcrConnectionManager>(serviceProvider, this),
                   LazyThreadSafetyMode.ExecutionAndPublication);

        _serializationRegistry = new Lazy<SerializationRegistry>(
            () => ActivatorUtilities.CreateInstance<SerializationRegistry>(serviceProvider, this),
                   LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Build-time snapshot of the public <see cref="GeodeClientOptions"/>
    /// into the internal <see cref="SystemProperties"/> bag (cppcache
    /// "geode.properties → SystemProperties at cache build"). Subsequent
    /// mutations to the caller's <paramref name="opts"/> do NOT affect
    /// this cache.
    /// </summary>
    private static SystemProperties BuildSystemProperties(GeodeClientOptions? opts)
    {
        if (opts is null) return new SystemProperties();

        // ── init-only properties: object initializer ────────────────
        var sp = new SystemProperties
        {
            Name = opts.Name,
            ThreadPoolSize = opts.ThreadPoolSize,

            // Subscription
            DurableClientId = opts.Subscription.DurableClientId,
            DurableTimeout = opts.Subscription.DurableTimeout,
            AutoReadyForEvents = opts.Subscription.AutoReadyForEvents,
            RedundancyMonitorInterval = opts.Subscription.RedundancyMonitorInterval,
            NotifyAckInterval = opts.Subscription.NotifyAckInterval,
            NotifyDupCheckLife = opts.Subscription.NotifyDupCheckLife,

            // Security
            SecurityClientDhAlgo = opts.Security.ClientDhAlgo,
            SecurityClientKsPath = opts.Security.ClientKsPath,
            SecurityProperties = opts.Security.Properties,

            // Heap (LRULimit: ulong public ↔ long internal — wire is i64 BE,
            // cast is safe within the positive-i64 range we care about).
            HeapLRULimit = (long)opts.Heap.LRULimit,
            HeapLRUDelta = opts.Heap.LRUDelta,

            // Tls — only Enabled has a SystemProperties analog today;
            // KeyStorePath / Password / TrustStorePath wire in when the
            // SSL handshake path lands (Phase 3+).
            SslEnabled = opts.Tls.Enabled,

            // Pool (these are pool-level wire knobs surfaced under
            // SystemProperties for cppcache parity — PoolAttributes
            // owns the per-pool overrides).
            ConnectionPoolSize = (uint)opts.Pool.ConnectionPoolSize,
            ConnectTimeout = opts.Pool.ConnectTimeout,
            ConnectWaitTimeout = opts.Pool.ConnectWaitTimeout,
            MaxSocketBufferSize = opts.Pool.MaxSocketBufferSize,
            PingInterval = opts.Pool.PingInterval,
            BucketWaitTimeout = opts.Pool.BucketWaitTimeout,
            DisableShufflingEndpoint = !opts.Pool.ShuffleEndpoints,   // inverted (cppcache parity)
        };

        // ── { get; set; } properties: assign after init-block ────────
        sp.MaxDepth = opts.Serialization.MaxDepth;
        sp.MaxArrayLength = opts.Serialization.MaxArrayLength;
        sp.MaxBytesLength = opts.Serialization.MaxBytesLength;
        sp.MaxStringLength = opts.Serialization.MaxStringLength;

        // TODO Phase 2+ — fields without a SystemProperties analog today:
        //   opts.EnableChunkHandlerThread   (.NET ThreadPool covers it, may stay unmapped)
        //   opts.Tls.KeyStorePath/Password/TrustStorePath (SSL handshake)
        //   opts.Subscription.ConflateEvents (subscription queue settings)
        //   opts.Heap.TombstoneTimeout (concurrency-checks / tombstones)
        //   opts.Pdx.ClearTypeIdsOnDisconnect (PDX type registry)
        //   opts.Tx.SuspendedTimeout (transactions, Phase 11+)

        return sp;
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        // ── 2. TCCM init ────────────────────────────────────────
        // Sets _isDurable from options.Subscription. In pool mode
        // (our MVP) the three background workers stay parked; this
        // is essentially a flag flip. Must complete before any pool
        // queries TCCM.IsDurable / haEnabled.
        await _tcrConnectionManager.Value.InitAsync(isPool: true, ct).ConfigureAwait(false);

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

    internal SystemProperties CacheProperties => _systemProperties;

    internal TypeRegistry TypeRegistry => _typeRegistry;
    internal PdxTypeRegistry PdxTypeRegistry => _pdxTypeRegistry;
    internal SerializationRegistry SerializationRegistry => _serializationRegistry.Value;
    internal TcrConnectionManager ConnectionManager => _tcrConnectionManager.Value;
    internal EventIdGenerator EventIdGenerator => _eventIdGenerator;

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
        //   TODO Phase 1.2: destroy regions (region drop happens between
        //     TCCM stop and pool close in cppcache).
        //
        // Pool drain — cascades pool.DestroyAsync into each
        // ThinClientPoolDM (cancels its conn-management loop, releases
        // timers, drains connections). PoolManager.CloseAsync is
        // internally idempotent so a later DI-scope dispose is safe.
        //
        // IsValueCreated guard: if no one ever read `PoolManager`, the
        // Lazy never materialised, so there's nothing to drain — skip
        // force-building one on the dispose path. (Critical when
        // DisposeAsync fires during ServiceProvider teardown: the SP is
        // already disposed and ActivatorUtilities.CreateInstance would
        // throw ObjectDisposedException.)
        if (_poolManager.IsValueCreated)
        {
            await _poolManager.Value.CloseAsync(keepAlive: false, ct).ConfigureAwait(false);
        }
        IsClosed = true;
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

    /// <summary>
    /// Mirrors cppcache <c>Cache::createRegionFactory(RegionShortcut)</c>
    /// (<c>cppcache/src/Cache.cpp</c> → <c>CacheImpl::createRegionFactory</c>).
    /// Direct <see langword="new"/> rather than ActivatorUtilities — we
    /// already hold every ctor arg, and the cache instance is the natural
    /// back-pointer for the factory's eventual register-on-create step.
    /// </summary>
    public RegionFactory CreateRegionFactory(RegionShortcut shortcut)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        return new RegionFactory(_serviceProvider, this, shortcut);
    }

    /// <summary>
    /// Register a freshly-built region on the cache. Called by
    /// <see cref="RegionFactory.CreateAsync{TKey, TValue}(string, CancellationToken)"/>;
    /// mirrors cppcache <c>CacheImpl::createRegion</c> map insertion
    /// (<c>cppcache/src/CacheImpl.cpp:395-398, 440</c>).
    /// </summary>
    internal void RegisterRegion(string name, IRegion region)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        if (!_regions.TryAdd(name, region))
        {
            throw new RegionExistsException(
                $"CacheImpl::createRegion: \"{name}\" region exists in local cache");
        }
    }

    /// <summary>
    /// Shared typed-result adapter used to wrap freshly-created regions
    /// in <see cref="RegionView{TKey, TValue}"/>; same instance the
    /// cache hands to <see cref="GetRegion{TKey, TValue}(string)"/>.
    /// </summary>
    internal Protocol.Serialization.TypedResultAdapter TypedResultAdapter => _typedResultAdapter;

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
            var defaultPool = _poolManager.Value.DefaultPool
                ?? throw new InvalidOperationException(
                    "Cache has no default pool — call EnsureInitializedAsync " +
                    "first or ensure at least one pool is registered.");
            return defaultPool.QueryService;
        }

        var pool = _poolManager.Value.Find(poolName)
            ?? throw new ArgumentException(
                $"Pool '{poolName}' is not registered.", nameof(poolName));
        return pool.QueryService;
    }



    public bool IsClosed { get; private set; }

    public string Name => _name;

    public IPoolManager PoolManager => _poolManager.Value;


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
