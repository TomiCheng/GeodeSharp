namespace Geode.Client;

/// <summary>
/// Fluent builder for client-side region attachments; obtain via
/// <see cref="IGeodeCache.CreateRegionFactory(RegionShortcut)"/>.
/// Mirrors cppcache <c>RegionFactory</c>
/// (<c>cppcache/include/geode/RegionFactory.hpp</c>).
/// </summary>
/// <remarks>
/// Setters are fluent (return <c>this</c>);
/// <see cref="CreateAsync{TKey, TValue}(string, CancellationToken)"/>
/// snapshots the attributes so further factory mutations don't affect
/// already-built regions. Listener / persistence / expiration /
/// cacheLoader / cacheWriter setters are deferred to Phase 2+.
/// </remarks>
public interface IRegionFactory
{
    /// <summary>Attach the region to the <see cref="IPool"/> named <paramref name="poolName"/>; empty falls back to the cache's default pool.</summary>
    IRegionFactory SetPoolName(string poolName);

    /// <summary>Initial bucket count of the local entry map; must be positive.</summary>
    IRegionFactory SetInitialCapacity(int initialCapacity);

    /// <summary>Load factor of the local entry map; must be positive.</summary>
    IRegionFactory SetLoadFactor(float loadFactor);

    /// <summary>Concurrency level of the local entry map; must be positive.</summary>
    IRegionFactory SetConcurrencyLevel(int concurrencyLevel);

    /// <summary>LRU cap on local entries; <c>0</c> (default) disables LRU eviction.</summary>
    IRegionFactory SetLruEntriesLimit(int entriesLimit);

    /// <summary>Whether to store entries locally; <see langword="false"/> means every op goes to the server.</summary>
    IRegionFactory SetCachingEnabled(bool cachingEnabled);

    /// <summary>Whether to clone the old value before applying a delta (default <see langword="false"/>).</summary>
    IRegionFactory SetCloningEnabled(bool cloningEnabled);

    /// <summary>Whether to run version checks on region entries (default <see langword="true"/>).</summary>
    IRegionFactory SetConcurrencyChecksEnabled(bool concurrencyChecksEnabled);

    /// <summary>Build the client-side region under <paramref name="name"/> and register it on the cache.</summary>
    /// <exception cref="RegionExistsException">A region with <paramref name="name"/> is already registered.</exception>
    /// <exception cref="ObjectDisposedException">The owning cache is closed.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or contains <c>'/'</c>.</exception>
    /// <exception cref="InvalidOperationException">The shortcut needs a server pool and either no <c>PoolName</c> was set (and the cache has no default pool) or the named pool is not registered.</exception>
    Task<IRegion<TKey, TValue>> CreateAsync<TKey, TValue>(string name, CancellationToken ct = default)
        where TKey : IEquatable<TKey>;
}
