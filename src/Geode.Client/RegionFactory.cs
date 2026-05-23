using Geode.Client.Internal;
using Geode.Client.Services;

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
    public Task<IRegion<TKey, TValue>> CreateAsync<TKey, TValue>(
        string name, CancellationToken ct = default)
        where TKey : IEquatable<TKey>
    {
        // TODO Phase 1.x:
        //   Step A. validate name (non-empty, no '/').
        //   Step B. snapshot _attrsFactory.Create() → immutable RegionAttributes.
        //   Step C. resolve pool: attrs.PoolName empty → _cache.PoolManager.DefaultPool;
        //           else _cache.PoolManager.Find(attrs.PoolName) (throw on miss).
        //   Step D. construct internal region by _shortcut:
        //             Proxy / CachingProxy(+LRU) → new ProxyRegion(...)
        //             Local / LocalEntryLru     → new LocalRegion(...)
        //           cppcache RegionFactory.cpp:60-77 — shortcut also flips
        //           cachingEnabled / lruEntriesLimit on the attrs before create.
        //   Step E. _cache.RegisterRegion(name, region) — TryAdd; throw
        //           RegionExistsException on duplicate.
        //   Step F. wrap in RegionView<TKey, TValue> for the typed surface.
        throw new NotImplementedException(
            "RegionFactory.CreateAsync is not yet implemented; Phase 1.x.");
    }
}
