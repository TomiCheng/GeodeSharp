using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client;

/// <summary>
/// Fluent builder for client-side region attachments; obtain via <see cref="IGeodeCache.CreateRegionFactory(RegionShortcut)"/>.
/// </summary>
/// <remarks>
/// Mirrors cppcache <c>RegionFactory</c>
/// (<c>cppcache/include/geode/RegionFactory.hpp</c>). Setters delegate to
/// an internal <see cref="RegionAttributesFactory"/>;
/// <see cref="CreateAsync{TKey, TValue}(string, CancellationToken)"/>
/// snapshots the attributes so further factory mutations don't affect
/// already-built regions.
/// Listener / persistence / expiration / cacheLoader / cacheWriter
/// setters are deferred to Phase 2+.
/// </remarks>
public class RegionFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GeodeCache _cache;
    private readonly RegionShortcut _shortcut;
    private readonly RegionAttributesFactory _attrsFactory = new();

    internal RegionFactory(
        IServiceProvider serviceProvider,
        GeodeCache cache,
        RegionShortcut shortcut)
    {
        _serviceProvider = serviceProvider;
        _cache = cache;
        _shortcut = shortcut;
        ApplyShortcutPresets();
    }

    /// <summary>
    /// Read-only snapshot of the in-progress attributes; used by tests
    /// to lock the shortcut-preset matrix. Not part of the public
    /// surface — RegionAttributes itself is <see langword="internal"/>.
    /// </summary>
    internal RegionAttributes SnapshotAttributes() => _attrsFactory.Create();

    /// <summary>The shortcut this factory was created with; test seam.</summary>
    internal RegionShortcut Shortcut => _shortcut;

    /// <summary>
    /// Mirrors cppcache <c>RegionFactory::setRegionShortcut()</c>
    /// (<c>cppcache/src/RegionFactory.cpp:60-80</c>): the shortcut
    /// pre-loads <c>cachingEnabled</c> and <c>lruEntriesLimit</c> on
    /// the underlying <see cref="RegionAttributesFactory"/> before the
    /// user gets to chain setters. Subsequent setter calls (e.g.
    /// <see cref="SetCachingEnabled(bool)"/>) override these presets,
    /// matching cppcache's "ctor first, setX last" precedence.
    /// </summary>
    private void ApplyShortcutPresets()
    {
        // cppcache CacheImpl.hpp:43 — DEFAULT_LRU_MAXIMUM_ENTRIES = 100000.
        // No .ini / XSD entry; per dont-invent-config-knobs.md memory we
        // hard-code rather than lift to Options.
        const int defaultLruMaximumEntries = 100000;

        switch (_shortcut)
        {
            case RegionShortcut.Proxy:
                _attrsFactory.SetCachingEnabled(false);
                break;
            case RegionShortcut.CachingProxy:
                _attrsFactory.SetCachingEnabled(true);
                break;
            case RegionShortcut.CachingProxyEntryLru:
                _attrsFactory.SetCachingEnabled(true);
                _attrsFactory.SetLruEntriesLimit(defaultLruMaximumEntries);
                break;
            case RegionShortcut.Local:
                // cppcache RegionFactory.cpp:73 — no presets; caching=true
                // (RegionAttributes default) carries through.
                break;
            case RegionShortcut.LocalEntryLru:
                _attrsFactory.SetLruEntriesLimit(defaultLruMaximumEntries);
                break;
        }
    }

    /// <summary>Attach the region to the <see cref="IPool"/> named <paramref name="poolName"/>; empty falls back to the cache's default pool.</summary>
    public RegionFactory SetPoolName(string poolName)
    {
        _attrsFactory.SetPoolName(poolName);
        return this;
    }

    /// <summary>Initial bucket count of the local entry map; must be positive.</summary>
    public RegionFactory SetInitialCapacity(int initialCapacity)
    {
        _attrsFactory.SetInitialCapacity(initialCapacity);
        return this;
    }

    /// <summary>Load factor of the local entry map; must be positive.</summary>
    public RegionFactory SetLoadFactor(float loadFactor)
    {
        _attrsFactory.SetLoadFactor(loadFactor);
        return this;
    }

    /// <summary>Concurrency level of the local entry map; must be positive.</summary>
    public RegionFactory SetConcurrencyLevel(int concurrencyLevel)
    {
        _attrsFactory.SetConcurrencyLevel(concurrencyLevel);
        return this;
    }

    /// <summary>LRU cap on local entries; <c>0</c> (default) disables LRU eviction.</summary>
    public RegionFactory SetLruEntriesLimit(int entriesLimit)
    {
        _attrsFactory.SetLruEntriesLimit(entriesLimit);
        return this;
    }

    /// <summary>Whether to store entries locally; <see langword="false"/> means every op goes to the server.</summary>
    public RegionFactory SetCachingEnabled(bool cachingEnabled)
    {
        _attrsFactory.SetCachingEnabled(cachingEnabled);
        return this;
    }

    /// <summary>Whether to clone the old value before applying a delta (default <see langword="false"/>).</summary>
    public RegionFactory SetCloningEnabled(bool cloningEnabled)
    {
        _attrsFactory.SetCloningEnabled(cloningEnabled);
        return this;
    }

    /// <summary>Whether to run version checks on region entries (default <see langword="true"/>).</summary>
    public RegionFactory SetConcurrencyChecksEnabled(bool concurrencyChecksEnabled)
    {
        _attrsFactory.SetConcurrencyChecksEnabled(concurrencyChecksEnabled);
        return this;
    }

    /// <summary>Build the client-side region under <paramref name="name"/> and register it on the cache.</summary>
    /// <exception cref="RegionExistsException">A region with <paramref name="name"/> is already registered.</exception>
    /// <exception cref="ObjectDisposedException">The owning cache is closed.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains <c>'/'</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The shortcut needs a server pool and either no <c>PoolName</c> was set
    /// (and the cache has no default pool) or the named pool is not registered.
    /// </exception>
    public Task<IRegion<TKey, TValue>> CreateAsync<TKey, TValue>(
        string name, CancellationToken ct = default)
        where TKey : IEquatable<TKey>
    {
        // ── Step 1. Validate name. Mirrors cppcache
        //   CacheImpl::createRegion (cppcache/src/CacheImpl.cpp:385-388):
        //   "Malformed name string, contains region path seperator '/'".
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('/'))
        {
            throw new ArgumentException(
                "Malformed name string, contains region path seperator '/'",
                nameof(name));
        }

        ObjectDisposedException.ThrowIf(_cache.IsClosed, _cache);

        // ── Step 2. Snapshot attrs (cppcache
        //   RegionFactory.cpp:45 — m_regionAttributesFactory->create()).
        var attrs = _attrsFactory.Create();

        // ── Step 3. Auto-fill PoolName from the cache's default pool for
        //   shortcuts that need a server. cppcache RegionFactory.cpp:46
        //   excludes ONLY RegionShortcut::LOCAL from this check;
        //   LocalEntryLru still needs a pool (cppcache quirk, mirrored
        //   for parity).
        if (_shortcut != RegionShortcut.Local && string.IsNullOrEmpty(attrs.PoolName))
        {
            var defaultPool = _cache.PoolManager.DefaultPool
                ?? throw new InvalidOperationException("No pool for non-local region.");
            // cppcache RegionFactory.cpp:52-53 writes the resolved name
            // back so the region carries it on its attrs snapshot.
            attrs.PoolName = ((ThinClientPoolDM)defaultPool).Name;
        }

        // ── Step 4. Local shortcut needs a server-less Region impl
        //   (cppcache CacheImpl.cpp:526 branches to LocalRegion when
        //   attrs.PoolName is empty). We have no concrete LocalRegion
        //   yet — Phase 2+.
        if (_shortcut == RegionShortcut.Local)
        {
            throw new NotImplementedException(
                "RegionShortcut.Local needs a server-less Region implementation; Phase 2+.");
        }

        // ── Step 5. Resolve the actual pool the region attaches to.
        var pool = _cache.PoolManager.Find(attrs.PoolName)
            ?? throw new InvalidOperationException(
                $"Pool '{attrs.PoolName}' is not registered.");
        var tcrPool = (ThinClientPoolDM)pool;

        // ── Step 6. Build the server-backed region. ActivatorUtilities
        //   so DI fills IServiceProvider + ILogger<ThinClientRegion>;
        //   we pass the per-call args (name, attrs, dm) explicitly.
        var region = ActivatorUtilities.CreateInstance<ThinClientRegion>(
            _serviceProvider, name, attrs, (ThinClientBaseDM)tcrPool);

        // ── Step 7. Register on cache (cppcache CacheImpl::createRegion
        //   m_regions.emplace, cppcache/src/CacheImpl.cpp:440). Throws
        //   RegionExistsException on duplicate name.
        _cache.RegisterRegion(name, region);

        // TODO Phase 2+: cppcache CacheImpl.cpp:447-458 — when
        //   pool.PrSingleHopEnabled, enqueue the region's full-path with
        //   the pool's ClientMetadataService for initial single-hop
        //   metadata refresh.

        // ── Step 8. Wrap in typed view for the IRegion<TKey, TValue> contract.
        return Task.FromResult<IRegion<TKey, TValue>>(
            new RegionView<TKey, TValue>(region, _cache.TypedResultAdapter));
    }
}
