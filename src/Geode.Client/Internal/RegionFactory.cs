using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Concrete <see cref="IRegionFactory"/>; constructed by
/// <see cref="GeodeCache.CreateRegionFactory(RegionShortcut)"/> through
/// <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/>.
/// Mirrors cppcache <c>RegionFactory</c>
/// (<c>cppcache/src/RegionFactory.cpp</c>) — setters delegate to an
/// internal <see cref="RegionAttributesFactory"/>;
/// <see cref="CreateAsync{TKey, TValue}"/> snapshots the attributes so
/// further factory mutations don't affect already-built regions.
/// </summary>
/// <remarks>
/// Class is <see langword="internal"/> + primary ctor so the IL ctor
/// surfaces as public to <see cref="ActivatorUtilities"/> (which
/// otherwise only sees public ctors) while staying hidden from
/// external callers. The <see cref="IRegionFactory"/> interface is
/// the only public reference point.
/// </remarks>
internal class RegionFactory(IServiceProvider serviceProvider, RegionShortcut shortcut)
    : IRegionFactory
{
    private readonly RegionAttributesFactory _attrsFactory = BuildPresetAttrsFactory(shortcut);

    /// <summary>
    /// Read-only snapshot of the in-progress attributes; used by tests
    /// to lock the shortcut-preset matrix. Not part of the public
    /// surface — RegionAttributes itself is <see langword="internal"/>.
    /// </summary>
    internal RegionAttributes SnapshotAttributes() => _attrsFactory.Create();

    /// <summary>The shortcut this factory was created with; test seam.</summary>
    internal RegionShortcut Shortcut => shortcut;

    /// <summary>
    /// Mirrors cppcache <c>RegionFactory::setRegionShortcut()</c>
    /// (<c>cppcache/src/RegionFactory.cpp:60-80</c>): pre-load
    /// <c>cachingEnabled</c> and <c>lruEntriesLimit</c> on a fresh
    /// <see cref="RegionAttributesFactory"/> based on the shortcut.
    /// Static so it can run as a field initialiser under the primary
    /// ctor (primary ctors don't allow a body); subsequent setter
    /// calls override these presets, matching cppcache's "ctor first,
    /// setX last" precedence.
    /// </summary>
    private static RegionAttributesFactory BuildPresetAttrsFactory(RegionShortcut shortcut)
    {
        // cppcache CacheImpl.hpp:43 — DEFAULT_LRU_MAXIMUM_ENTRIES = 100000.
        // No .ini / XSD entry; per dont-invent-config-knobs.md memory we
        // hard-code rather than lift to Options.
        const int defaultLruMaximumEntries = 100000;

        var attrs = new RegionAttributesFactory();
        switch (shortcut)
        {
            case RegionShortcut.Proxy:
                attrs.SetCachingEnabled(false);
                break;
            case RegionShortcut.CachingProxy:
                attrs.SetCachingEnabled(true);
                break;
            case RegionShortcut.CachingProxyEntryLru:
                attrs.SetCachingEnabled(true);
                attrs.SetLruEntriesLimit(defaultLruMaximumEntries);
                break;
            case RegionShortcut.Local:
                // cppcache RegionFactory.cpp:73 — no presets; caching=true
                // (RegionAttributes default) carries through.
                break;
            case RegionShortcut.LocalEntryLru:
                attrs.SetLruEntriesLimit(defaultLruMaximumEntries);
                break;
        }
        return attrs;
    }

    /// <inheritdoc />
    public IRegionFactory SetPoolName(string poolName)
    {
        _attrsFactory.SetPoolName(poolName);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetInitialCapacity(int initialCapacity)
    {
        _attrsFactory.SetInitialCapacity(initialCapacity);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetLoadFactor(float loadFactor)
    {
        _attrsFactory.SetLoadFactor(loadFactor);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetConcurrencyLevel(int concurrencyLevel)
    {
        _attrsFactory.SetConcurrencyLevel(concurrencyLevel);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetLruEntriesLimit(int entriesLimit)
    {
        _attrsFactory.SetLruEntriesLimit(entriesLimit);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetCachingEnabled(bool cachingEnabled)
    {
        _attrsFactory.SetCachingEnabled(cachingEnabled);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetCloningEnabled(bool cloningEnabled)
    {
        _attrsFactory.SetCloningEnabled(cloningEnabled);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetConcurrencyChecksEnabled(bool concurrencyChecksEnabled)
    {
        _attrsFactory.SetConcurrencyChecksEnabled(concurrencyChecksEnabled);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetCacheLoader(ICacheLoader cacheLoader)
    {
        ArgumentNullException.ThrowIfNull(cacheLoader);
        _attrsFactory.SetCacheLoader(cacheLoader);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetCacheWriter(ICacheWriter cacheWriter)
    {
        ArgumentNullException.ThrowIfNull(cacheWriter);
        _attrsFactory.SetCacheWriter(cacheWriter);
        return this;
    }

    /// <inheritdoc />
    public IRegionFactory SetCacheListener(ICacheListener cacheListener)
    {
        ArgumentNullException.ThrowIfNull(cacheListener);
        _attrsFactory.SetCacheListener(cacheListener);
        return this;
    }

    public IRegionFactory SetDiskPolicy(CacheDiskPolicy diskPolicy)
    {
        _attrsFactory.SetDiskPolicy(diskPolicy);
        return this;
    }

    public IRegionFactory SetPersistenceManager(IPersistenceManager persistenceManager)
    {
        ArgumentNullException.ThrowIfNull(persistenceManager);
        _attrsFactory.SetPersistenceManager(persistenceManager);
        return this;
    }

    /// <inheritdoc />
    public async Task<IRegion<TKey, TValue>> CreateAsync<TKey, TValue>(string name, CancellationToken ct = default)
        where TKey : IEquatable<TKey>
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Contains('/'))
        {
            throw new ArgumentException("Malformed name string, contains region path seperator '/'", nameof(name));
        }

        var cache = serviceProvider.GetRequiredService<GeodeCache>();
        ObjectDisposedException.ThrowIf(cache.IsClosed, cache);

        var attrs = _attrsFactory.Create();

        // cppcache RegionFactory.cpp:46 only checks `!= LOCAL` and so
        // throws for LOCAL_ENTRY_LRU too — looks like an upstream
        // oversight (LOCAL_ENTRY_LRU is conceptually local-only, no
        // wire / pool requirement). Modernised: both local shortcuts
        // skip the pool guard.
        if (shortcut is not (RegionShortcut.Local or RegionShortcut.LocalEntryLru)
            && string.IsNullOrEmpty(attrs.PoolName))
        {
            var defaultPool = cache.PoolManager.DefaultPool
                ?? throw new InvalidOperationException("No pool for non-local region.");
            attrs.PoolName = ((ThinClientPoolDM)defaultPool).Name;
        }

        var region = await cache.CreateRegionAsync(name, attrs, ct).ConfigureAwait(false);

        return ActivatorUtilities.CreateInstance<RegionView<TKey, TValue>>(serviceProvider, region);
    }
}
