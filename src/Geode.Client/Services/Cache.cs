using System.Collections.Concurrent;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

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

internal sealed class Cache : IGeodeCache
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GeodeClientOptions _options;
    private readonly ClientProxyMembershipIdBuilder _membershipIdBuilder;
    private readonly PoolManager _poolManager;
    private readonly TcrConnectionManager _tcrConnectionManager;

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
    private object? _remoteQueryService;  // m_remoteQueryServicePtr

    // ── Transactions (CacheImpl.hpp:376) ──
    private object? _cacheTransactionManager; // m_cacheTXManager

    // ── PDX / serialization (CacheImpl.hpp:323-324, 379-383) ──
    private bool _pdxIgnoreUnreadFields;  // m_ignorePdxUnreadFields
    private bool _pdxReadSerialized;      // m_readPdxSerialized
    private object? _pdxTypeRegistry;     // m_pdxTypeRegistry
    private object? _serializationRegistry;// m_serializationRegistry
    private object? _typeRegistry;        // m_typeRegistry

    // ── Versioning (CacheImpl.hpp:378) ──
    private object? _memberListForVersionStamp; // m_memberListForVersionStamp

    // ── Partition-routing flags (CacheImpl.hpp:320-322) ──
    private int _networkHop;              // m_networkhop (Interlocked 0/1)
    private int _prMetadataUpdated;       // m_pr_metadata_updated (Interlocked 0/1)
    private int _serverGroupFlag;         // m_serverGroupFlag (Interlocked int8_t)

    // ── Auth (CacheImpl.hpp:382) ──
    private object? _authInitialize;      // m_authInitialize

#pragma warning restore CS0169, CS0414, CS0649

    public Cache(
        IServiceProvider serviceProvider,
        CacheScopeContext scopeContext,
        ClientProxyMembershipIdBuilder membershipIdBuilder,
        PoolManager poolManager,
        TcrConnectionManager tcrConnectionManager)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(scopeContext);
        ArgumentNullException.ThrowIfNull(membershipIdBuilder);
        ArgumentNullException.ThrowIfNull(poolManager);
        ArgumentNullException.ThrowIfNull(tcrConnectionManager);

        Name = scopeContext.Name;
        _serviceProvider = serviceProvider;
        _options = scopeContext.Options;
        _membershipIdBuilder = membershipIdBuilder;
        _poolManager = poolManager;
        _tcrConnectionManager = tcrConnectionManager;
    }

    public string Name { get; }

    /// <summary>
    /// Test-only escape hatch: expose the scoped <see cref="PoolManager"/>
    /// so integration tests can reach <see cref="ThinClientPoolDM"/>
    /// internals (e.g. <c>PoolSize</c>) without DI scope wrangling. Not
    /// part of the public API — gated by <c>InternalsVisibleTo</c>.
    /// </summary>
    internal PoolManager PoolManager => _poolManager;

    public bool IsClosed { get; private set; }

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
    /// cppcache programmatic API. <c>_options.CacheXml is null</c>.
    /// </para>
    /// <para>
    /// <b>Path (a)</b>: caller supplied declarative cache.xml-style
    /// config — equivalent to cppcache
    /// <c>initializeDeclarativeCache()</c>.
    /// <c>_options.CacheXml is not null</c>.
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
        await _tcrConnectionManager.InitAsync(isPool: true, ct).ConfigureAwait(false);

        // ── 3-5. Build and init pools ───────────────────────────
        // Both paths produce a sequence of CacheXmlPoolOptions; the
        // foreach below builds + inits each one uniformly. Multi-pool /
        // multi-server / locator gating now lives inside
        // ThinClientPoolDM's ctor, so Cache stays generic. Required-
        // field validation is the Options layer's job (Phase 1.1 收尾);
        // here we trust the input.
        if (_options.CacheXml is null)
        {
            // path (b) — Options-based (programmatic, the default).
            // TODO step 3.b: enumerate a yet-to-be-added programmatic
            //   pool-config surface (e.g. _options.Pools) and project
            //   into CacheXmlPoolOptions-shape items.
            throw new NotImplementedException(
                "TODO: Cache.InitializeCoreAsync step 3.b (path b — Options-based)");

            // Step 4 and 5
        }
        else
        {
            // path (a) — Declarative cache.xml-style.
            // cppcache equivalent: initializeDeclarativeCache(xml)
            //   → xmlParser->create() builds pools from <pool> elements.
            foreach (var xmlPool in _options.CacheXml.Pools)
            {
                // ── 4. Build ThinClientPoolDM + register ────────────
                // ctor enforces Phase 1.5 deferred limits (multi-server
                // / locator) internally; here we just hand it the xml
                // pool config and the shared TCCM.
                // Positional args match ThinClientPoolDM's primary ctor
                // (xmlPool + options + TCCM); ILogger is filled by DI.
                var pool = ActivatorUtilities.CreateInstance<ThinClientPoolDM>(
                    _serviceProvider, xmlPool, _options, _tcrConnectionManager);
                _poolManager.AddPool(xmlPool.Name, pool);

                // ── 5. Init pool — real TCP / handshake fires here ──
                // Pool.InitAsync internally:
                //   • locator query → endpoint list, OR direct server list
                //   • foreach endpoint → TcrEndpoint.CreateNewConnectionAsync(...)
                //       • socket open + handshake bytes
                //       • receive server-issued uniqueId
                //   • mark pool ready
                await pool.InitAsync(ct).ConfigureAwait(false);
            }
        }

        // ── 6. PDX / serialization registration (Phase 2+) ──────
        // TODO: if (_options.CacheXml?.Pdx is { } pdx) apply pdx
        //   ignoreUnreadFields / readSerialized to _pdxTypeRegistry.
    }

    public IRegion<TKey, TValue>? GetRegion<TKey, TValue>(string path)
        where TKey : notnull
    {
        // Untyped lookup does the cppcache-faithful work (path validation,
        // sub-region recursion, destroyPending check). RegionView is a
        // pure compile-time wrapper — TKey/TValue are not runtime-bound.
        var region = GetRegion(path);
        return region is null ? null : new RegionView<TKey, TValue>(region);
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
        await _poolManager.CloseAsync(keepAlive: false, ct).ConfigureAwait(false);

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
}
