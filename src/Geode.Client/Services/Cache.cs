using System.Collections.Concurrent;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Pdx;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCache"/> implementation. One instance per
/// registered name (cached by <see cref="GeodeCacheFactory"/>).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors cppcache <c>Cache</c>
/// (<c>cppcache/include/geode/Cache.hpp</c>) &#x2014; the concrete bottom of
/// the upstream <c>RegionService</c> &#x2192; <c>GeodeCache</c>
/// &#x2192; <c>Cache</c> hierarchy. cppcache's Pimpl split
/// (<c>Cache</c> façade + <c>CacheImpl</c> body) is collapsed here:
/// .NET doesn't need the binary-compatibility shim, so this single
/// class plays both roles.
/// </para>
/// <para>
/// Member fields mirror cppcache <c>CacheImpl.hpp:319-384</c> 1:1 per
/// CLAUDE.md "mirror then prune". Owning types we have not built yet
/// are typed as <c>object?</c> placeholders &#x2014; replace with the
/// real type when its phase ships, or delete the field if never used.
/// Bucket-1 fields (<c>m_expiryTaskManager</c>, <c>m_statisticsManager</c>,
/// <c>m_threadPool</c>, <c>m_evictionController</c>, <c>m_adminRegion</c>,
/// <c>m_cacheStats</c>) are intentionally omitted &#x2014; .NET BCL
/// covers them. The Pimpl back-pointer <c>m_cache</c> is also omitted
/// because the split is collapsed.
/// </para>
/// </remarks>
///

internal sealed class Cache(
    IServiceProvider serviceProvider,
    CacheScopeContext scopeContext,
    //ClientProxyMembershipIdBuilder membershipIdBuilder,
    PoolManager poolManager,
    TcrConnectionManager tcrConnectionManager,
    TypedResultAdapter typedResultAdapter) : IGeodeCache
{

    /// <summary>
    /// <c>SemaphoreSlim</c>-gated double-checked init. cppcache
    /// equivalent is the <c>m_initDone</c> + <c>m_initDoneLock</c>
    /// guard inside <c>CacheImpl::createRegion</c> /
    /// <c>getQueryService</c>. Chosen over <c>Lazy&lt;Task&gt;(EAP)</c>
    /// so:
    /// <list type="bullet">
    ///   <item>the first caller's ct reaches
    ///         <see cref="InitializeCoreAsync"/>;</item>
    ///   <item>each later caller awaits via
    ///         <see cref="Task.WaitAsync(CancellationToken)"/> using
    ///         their own ct &#x2014; cancelling that wait does not
    ///         cancel the underlying init;</item>
    ///   <item>on failure, <c>_initTask</c> can be reset to null to
    ///         allow retry (cppcache <c>m_initDone</c> stays false on
    ///         throw — same semantics).</item>
    /// </list>
    /// </summary>
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Task? _initTask;
    private readonly GeodeClientOptions _options = scopeContext.Options;

    private ITypeRegistry? _typeRegistry;

    /// <summary>
    /// Runs once via <see cref="EnsureInitializedAsync"/>. Two config
    /// sources converge on the same in-memory pool / region registry.
    /// cppcache splits them by sync timing
    /// (<c>CacheFactory::create</c> body); we unify under one async
    /// method so ctor never blocks on I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Path (b)</b>: caller used <see cref="PoolOptions"/> /
    /// <c>Action&lt;GeodeClientOptions&gt;</c> — equivalent to
    /// cppcache programmatic API. <c>_options.Cache is null</c>.
    /// </para>
    /// <para>
    /// <b>Path (a)</b>: caller supplied declarative cache.xml-style
    /// config — equivalent to cppcache
    /// <c>initializeDeclarativeCache()</c>.
    /// <c>_options.Cache is not null</c>.
    /// </para>
    /// </remarks>
    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        // ── 1. Pre-check ────────────────────────────────────────
        if (IsClosed)
        {
            throw new ObjectDisposedException(nameof(Cache));
        }

        // ── 2. TCCM init ────────────────────────────────────────
        // Sets _isDurable from options.Subscription. In pool mode
        // (our MVP) the three background workers stay parked; this
        // is essentially a flag flip. Must complete before any pool
        // queries TCCM.IsDurable / haEnabled.
        await tcrConnectionManager.InitAsync(isPool: true, ct).ConfigureAwait(false);

        // ── 3-5. Build and init pools ───────────────────────────
        // Both paths produce a sequence of CachePoolOptions; the
        // foreach below builds + inits each one uniformly. Multi-pool /
        // multi-server / locator gating now lives inside
        // ThinClientPoolDM's ctor, so Cache stays generic. Required-
        // field validation is the Options layer's job (Phase 1.1 收尾);
        // here we trust the input.
        if (_options.Cache is null)
        {
            // path (b) — Options-based (programmatic, the default).
            // TODO step 3.b: enumerate a yet-to-be-added programmatic
            //   pool-config surface (e.g. _options.Pools) and project
            //   into CachePoolOptions-shape items.
            throw new NotImplementedException(
                "TODO: Cache.InitializeCoreAsync step 3.b (path b — Options-based)");
        }
        else
        {
            // path (a) — Declarative cache.xml-style. Mirrors cppcache
            // CacheImpl::initializeDeclarativeCache(xml).
            await InitializeDeclarativeCacheAsync(_options.Cache, ct).ConfigureAwait(false);
        }

        // ── 7. PDX / serialization registration (Phase 2+) ──────
        // TODO: if (_options.Cache?.Pdx is { } pdx) apply pdx
        //   ignoreUnreadFields / readSerialized to _pdxTypeRegistry.
    }

    /// <summary>
    /// Build pools and regions from an already-bound
    /// <see cref="CacheOptions"/> tree. Mirrors cppcache
    /// <c>CacheImpl::initializeDeclarativeCache(const std::string&amp;)</c>
    /// — the difference is we work off already-parsed options instead
    /// of running an XML parser (Xerces is bucket 1, cut per
    /// CLAUDE.md).
    /// </summary>
    /// <remarks>
    /// Two passes: pools first (so regions can resolve their pool
    /// references), then regions. Each pool's <c>InitAsync</c> opens
    /// real sockets — this is where I/O actually fires.
    /// </remarks>
    private async Task InitializeDeclarativeCacheAsync(CacheOptions cache, CancellationToken ct)
    {
        await InitializePoolsAsync(cache, ct).ConfigureAwait(false);

        // ── 6. Build regions ────────────────────────────────────
        // cppcache equivalent: CacheParser::create iterates
        // <region> elements and calls CacheImpl::createRegion(name,
        // attrs) for each top-level region (sub-regions handled
        // recursively in the parser itself).
        foreach (var xmlRegion in cache.Regions)
        {
            // Name structural validation (non-empty / non-whitespace)
            // and RefId existence are enforced by
            // GeodeClientOptionsValidator at host build time — no
            // inline checks needed here.

            // ── 6.1 Resolve refid template ─────────────────
            // cppcache CacheParser folds <region refid="..."> onto
            // a previously declared <region-attributes id="..."> at
            // parse time (CacheParser.cpp:777-786). We do the same
            // here: clone the template, then let xmlRegion.Attributes
            // override non-null / non-empty fields.
            var attributes = ResolveAttributes(xmlRegion, cache.NamedAttributes);

            // ── 6.2 Resolve pool ───────────────────────────
            // cppcache CacheImpl::createRegion_internal
            // (CacheImpl.cpp:524) looks up the pool by name; empty
            // PoolName falls through to PoolManager.DefaultPool
            // (Find("") returns DefaultPool).
            var pool = poolManager.Find(attributes.PoolName);
            if (pool is null)
            {
                // Either PoolName references a pool not declared in
                // Cache.Pools, or PoolName is empty and no pools
                // are registered (the validator should have caught
                // the second case; defensive guard).
                throw new InvalidOperationException(
                    $"Region '{xmlRegion.Name}' references pool " +
                    $"'{attributes.PoolName}' which is not registered " +
                    "(empty PoolName resolves to the default pool).");
            }

            // ── 6.3 IPool → ThinClientBaseDM ───────────────
            // MVP has only one IPool impl (ThinClientPoolDM, which
            // IS-A ThinClientBaseDM), so the cast is always safe
            // today. The pattern-match form gives a clearer error
            // message if a future non-DM IPool implementation
            // arrives (Phase 1.5+) than a raw InvalidCastException.
            if (pool is not ThinClientBaseDM dm)
            {
                throw new InvalidOperationException(
                    $"Pool '{attributes.PoolName}' " +
                    $"({pool.GetType().Name}) does not derive from " +
                    $"{nameof(ThinClientBaseDM)}; cannot be used as a " +
                    "region's distribution manager.");
            }

            // ── 6.4 Build ThinClientRegion ─────────────────
            // Phase 1.2 builds top-level regions only — `parent` is
            // always null until sub-region creation lands.
            // ActivatorUtilities can't match a null arg against the
            // `RegionInternal?` ctor slot (params object[] erases the
            // type), so resolve the logger from DI manually and call
            // the ctor directly. Mirrors what ActivatorUtilities would
            // have done minus the broken null-arg matching.
            var region = ActivatorUtilities.CreateInstance<ThinClientRegion>(
                serviceProvider,
                xmlRegion.Name,
                attributes,
                dm);

            // ── 6.5 Register ───────────────────────────────
            // cppcache CacheImpl::createRegion throws
            // RegionExistsException when m_regions already holds
            // the name. Future: GeodeClientOptionsValidator should
            // also flag duplicate names in Cache.Regions at
            // startup so this guard becomes pure belt-and-braces.
            if (!_regions.TryAdd(xmlRegion.Name, region))
            {
                throw new InvalidOperationException(
                    $"Region '{xmlRegion.Name}' is declared more than once " +
                    "in Cache.Regions.");
            }

            // ── 6.6 Sub-region children ────────────────────
            if (xmlRegion.ChildRegions.Count > 0)
            {
                // TODO: recurse into ChildRegions and build each as
                //   a sub-region of `region`. Mirrors cppcache
                //   CacheParser walking nested <region> elements
                //   and calling RegionInternal::createSubregion on
                //   the parent. Currently throws so XML-declared
                //   sub-regions aren't silently dropped.
                throw new NotImplementedException(
                    $"Region '{xmlRegion.Name}' declares " +
                    $"{xmlRegion.ChildRegions.Count} sub-region(s); " +
                    "sub-region creation is deferred to a later phase.");
            }
        }
    }

    /// <summary>
    /// Build and initialise every declared pool (or the synthesized
    /// default pool when <see cref="CacheOptions.Endpoints"/> is set
    /// instead). Real TCP / handshake fires inside each
    /// <c>InitAsync</c>.
    /// </summary>
    /// <remarks>
    /// cppcache <c>&lt;client-cache endpoints="..."&gt;</c> maps to
    /// <c>poolFactory_-&gt;addServer(...)</c>
    /// (<c>CacheXmlParser.cpp:553-560</c>). The validator guarantees
    /// <see cref="CacheOptions.Endpoints"/> and
    /// <see cref="CacheOptions.Pools"/> are mutually exclusive, so
    /// exactly one branch fires. The synthesized pool is built into
    /// a local list — we don't mutate the shared options instance,
    /// which would bleed across caches built from the same
    /// <c>IOptionsMonitor</c> snapshot.
    /// </remarks>
    private async Task InitializePoolsAsync(CacheOptions cache, CancellationToken ct)
    {
        foreach (var xmlPool in ResolvePoolsToBuild(cache))
        {
            // ctor enforces Phase 1.5 deferred limits (multi-server
            // / locator) internally; here we just hand it the pool
            // config and the shared TCCM. Positional args match
            // ThinClientPoolDM's primary ctor (xmlPool + options +
            // TCCM); ILogger is filled by DI.
            var pool = ActivatorUtilities.CreateInstance<ThinClientPoolDM>(
                serviceProvider, xmlPool, _options, tcrConnectionManager);
            poolManager.AddPool(xmlPool.Name, pool);

            // Pool.InitAsync internally:
            //   • locator query → endpoint list, OR direct server list
            //   • foreach endpoint → TcrEndpoint.CreateNewConnectionAsync(...)
            //       • socket open + handshake bytes
            //       • receive server-issued uniqueId
            //   • mark pool ready
            await pool.InitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Apply a refid template (if any) and merge the region's inline
    /// attribute overrides on top. Mirrors cppcache
    /// <c>CacheParser</c> refid handling
    /// (<c>CacheParser.cpp:777-786</c>): non-empty
    /// <see cref="CacheRegionOptions.RefId"/> clones the named
    /// template; inline <see cref="CacheRegionOptions.Attributes"/>
    /// then overrides each field that is non-null (for value-type
    /// nullables) or non-empty (for plain strings).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chained refid is not honoured — a template's own
    /// <see cref="CacheRegionAttributesOptions.RefId"/> is ignored;
    /// templates must be self-contained.
    /// </para>
    /// <para>
    /// Returns <paramref name="xmlRegion"/>'s
    /// <see cref="CacheRegionOptions.Attributes"/> verbatim (same
    /// reference) when there is no <c>RefId</c> — no merge work, no
    /// allocation.
    /// </para>
    /// </remarks>
    private static CacheRegionAttributesOptions ResolveAttributes(
        CacheRegionOptions xmlRegion,
        IReadOnlyDictionary<string, CacheRegionAttributesOptions> namedAttributes)
    {
        if (string.IsNullOrEmpty(xmlRegion.RefId))
        {
            return xmlRegion.Attributes;
        }

        // Validator already enforces RefId membership; defensive guard
        // covers callers that bypass DI validation.
        if (!namedAttributes.TryGetValue(xmlRegion.RefId, out var template))
        {
            throw new InvalidOperationException(
                $"Region '{xmlRegion.Name}' RefId='{xmlRegion.RefId}' " +
                "does not match any key in Cache.NamedAttributes.");
        }

        var inline = xmlRegion.Attributes;
        return new CacheRegionAttributesOptions
        {
            // Nullable value types: inline non-null wins.
            CachingEnabled = inline.CachingEnabled ?? template.CachingEnabled,
            CloningEnabled = inline.CloningEnabled ?? template.CloningEnabled,
            Scope = inline.Scope ?? template.Scope,
            InitialCapacity = inline.InitialCapacity ?? template.InitialCapacity,
            LoadFactor = inline.LoadFactor ?? template.LoadFactor,
            ConcurrencyLevel = inline.ConcurrencyLevel ?? template.ConcurrencyLevel,
            LruEntriesLimit = inline.LruEntriesLimit ?? template.LruEntriesLimit,
            DiskPolicy = inline.DiskPolicy ?? template.DiskPolicy,
            ClientNotification = inline.ClientNotification ?? template.ClientNotification,
            ConcurrencyChecksEnabled = inline.ConcurrencyChecksEnabled ?? template.ConcurrencyChecksEnabled,

            // Plain strings: inline non-empty wins.
            Endpoints = string.IsNullOrEmpty(inline.Endpoints) ? template.Endpoints : inline.Endpoints,
            PoolName = string.IsNullOrEmpty(inline.PoolName) ? template.PoolName : inline.PoolName,

            // Inner RefId is not honoured (mirrors decision in
            // CacheRegionAttributesOptions doc); leave empty so the
            // resolved attributes don't accidentally trigger a second
            // round of resolution somewhere.
            RefId = string.Empty,

            // Reference types: inline non-null replaces wholesale (no deep merge).
            RegionTimeToLive = inline.RegionTimeToLive ?? template.RegionTimeToLive,
            RegionIdleTime = inline.RegionIdleTime ?? template.RegionIdleTime,
            EntryTimeToLive = inline.EntryTimeToLive ?? template.EntryTimeToLive,
            EntryIdleTime = inline.EntryIdleTime ?? template.EntryIdleTime,
            PartitionResolver = inline.PartitionResolver ?? template.PartitionResolver,
            CacheLoader = inline.CacheLoader ?? template.CacheLoader,
            CacheListener = inline.CacheListener ?? template.CacheListener,
            CacheWriter = inline.CacheWriter ?? template.CacheWriter,
            PersistenceManager = inline.PersistenceManager ?? template.PersistenceManager,
        };
    }

    /// <summary>
    /// Pure projection from <see cref="CacheOptions"/> to the list of
    /// pools the cache should build. When <see cref="CacheOptions.Endpoints"/>
    /// is non-empty, synthesises a single <c>"default"</c>-named
    /// <see cref="CachePoolOptions"/> whose <see cref="CachePoolOptions.Servers"/>
    /// is a deep copy of the endpoint list; otherwise returns
    /// <see cref="CacheOptions.Pools"/> as-is. Validator guarantees
    /// the two are mutually exclusive.
    /// </summary>
    internal static IReadOnlyList<CachePoolOptions> ResolvePoolsToBuild(CacheOptions cache)
    {
        if (cache.Endpoints.Count == 0) return cache.Pools;

        return
        [
            new()
            {
                Name = "default",
                Servers = cache.Endpoints.Select(e => e.Clone()).ToList(),
            },
        ];
    }

    /// <summary>
    /// Test-only escape hatch: expose the scoped <see cref="PoolManager"/>
    /// so integration tests can reach <see cref="ThinClientPoolDM"/>
    /// internals (e.g. <c>PoolSize</c>) without DI scope wrangling. Not
    /// part of the public API — gated by <c>InternalsVisibleTo</c>.
    /// </summary>
    internal PoolManager PoolManager => poolManager;

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
        await poolManager.CloseAsync(keepAlive: false, ct).ConfigureAwait(false);

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

    public async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        // Outer fast-path: once init started, every caller awaits the
        // shared Task. Volatile.Read pairs with the Volatile.Write
        // inside the lock so the publish is observable without
        // re-acquiring the semaphore.
        var task = Volatile.Read(ref _initTask);
        if (task is null)
        {
            await _initLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Double-check: a concurrent caller may have set it
                // while we waited on the semaphore.
                task = _initTask;
                if (task is null)
                {
                    // Start the init under the lock. The first caller's
                    // ct flows into InitializeCoreAsync; later callers
                    // observe their own ct only via WaitAsync below.
                    task = InitializeCoreAsync(ct);
                    Volatile.Write(ref _initTask, task);
                }
            }
            finally
            {
                _initLock.Release();
            }
        }
        // Per-caller cancellation: WaitAsync(ct) cancels *this* await,
        // not the underlying init Task. Other callers keep waiting.
        await task.WaitAsync(ct).ConfigureAwait(false);
    }

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
            var defaultPool = poolManager.DefaultPool
                ?? throw new InvalidOperationException(
                    "Cache has no default pool — call EnsureInitializedAsync " +
                    "first or ensure at least one pool is registered.");
            return defaultPool.QueryService;
        }

        var pool = poolManager.Find(poolName)
            ?? throw new ArgumentException(
                $"Pool '{poolName}' is not registered.", nameof(poolName));
        return pool.QueryService;
    }

    public IRegion<TKey, TValue>? GetRegion<TKey, TValue>(string path)
        where TKey : IEquatable<TKey>
    {
        // Untyped lookup does the cppcache-faithful work (path validation,
        // sub-region recursion, destroyPending check). RegionView is a
        // pure compile-time wrapper — TKey/TValue are not runtime-bound.
        var region = GetRegion(path);
        return region is null ? null : new RegionView<TKey, TValue>(region, typedResultAdapter);
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
            throw new ArgumentException(
                "Cache.GetRegion: path is empty or '/'.", nameof(path));
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

    public bool IsClosed { get; private set; }
    public string Name { get; } = scopeContext.Name;
    public ITypeRegistry TypeRegistry => LazyInitializer.EnsureInitialized(
        ref _typeRegistry,
        () => ActivatorUtilities.CreateInstance<TypeRegistry>(serviceProvider, this));

    public bool PdxIgnoreUnreadFields => _options.Cache?.Pdx.IgnoreUnreadFields ?? false;
    public bool PdxReadSerialized => _options.Cache?.Pdx.ReadSerialized ?? false;


#pragma warning disable CS0169, CS0414, CS0649 // placeholder fields mirroring CacheImpl; wired up phase by phase

    // ── Lifecycle (CacheImpl.hpp:359-374) ──
    // m_closed       → IsClosed property (already exposed)
    // m_initialized  → captured by _initTask (null = not started)
    // m_initDoneLock → _initLock (SemaphoreSlim, async-friendly)
    // m_destroyCacheMutex → bucket 1, replaced by System.Threading.Lock
    private int _destroyPending;          // m_destroyPending (Interlocked 0/1)
    private bool _keepAlive;              // m_keepAlive

    // ── Region registry (CacheImpl.hpp:364-366) ──
    // cppcache m_regions is std::map<string, shared_ptr<Region>>; we
    // hold the non-generic IRegion base because XML-driven population
    // happens before TKey/TValue are known.
    private readonly ConcurrentDictionary<string, IRegion> _regions = new(StringComparer.Ordinal);

    // ── Connection / Pool (CacheImpl.hpp:330, 362-363, 369) ──
    private object? _distributedSystem;   // m_distributedSystem
    // m_tcrConnectionManager / m_poolManager / m_clientProxyMembershipIDFactory
    //                                  → fields above (DI / Cache-owned)

    // ── Query (CacheImpl.hpp:370) ──
    // cppcache m_remoteQueryServicePtr is the non-pool fallback —
    // CacheImpl owns its own RemoteQueryService when no default pool
    // exists. We are pool-only (memory pool-only-no-non-pool.md), so
    // GetQueryService always delegates to PoolManager and never builds
    // a cache-owned service. The cppcache field has no .NET counterpart.

    // ── Transactions (CacheImpl.hpp:376) ──
    private object? _cacheTransactionManager; // m_cacheTXManager

    // ── PDX / serialization (CacheImpl.hpp:323-324, 379-383) ──
    private bool _pdxIgnoreUnreadFields;  // m_ignorePdxUnreadFields
    private bool _pdxReadSerialized;      // m_readPdxSerialized
    private object? _pdxTypeRegistry;     // m_pdxTypeRegistry
    private object? _serializationRegistry;// m_serializationRegistry

    // ── Versioning (CacheImpl.hpp:378) ──
    private object? _memberListForVersionStamp; // m_memberListForVersionStamp

    // ── Partition-routing flags (CacheImpl.hpp:320-322) ──
    private int _networkHop;              // m_networkhop (Interlocked 0/1)
    private int _prMetadataUpdated;       // m_pr_metadata_updated (Interlocked 0/1)
    private int _serverGroupFlag;         // m_serverGroupFlag (Interlocked int8_t)

    // ── Auth (CacheImpl.hpp:382) ──
    private object? _authInitialize;      // m_authInitialize

#pragma warning restore CS0169, CS0414, CS0649


}
