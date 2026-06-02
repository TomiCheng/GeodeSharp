namespace Geode.Client.Internal;

/// <summary>
/// Mutable builder for <see cref="RegionAttributes"/>; the inner layer of
/// the cppcache region-creation 3-tuple
/// (<c>RegionFactory</c> → <c>RegionAttributesFactory</c> → <c>RegionAttributes</c>).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors cppcache <c>RegionAttributesFactory</c>
/// (<c>cppcache/include/geode/RegionAttributesFactory.hpp</c>). cppcache
/// keeps this layer public so <c>cache.xml</c> parsing and the
/// sub-region API (<c>Region::createSubregion(name, attrs)</c>) can build
/// attributes standalone; both Phase 2+ for us, so the class is
/// <see langword="internal"/> until a real consumer surfaces.
/// </para>
/// <para>
/// Setters mirror the ones exposed through <see cref="RegionFactory"/>;
/// expiration / listener / persistence / partition-resolver / cacheWriter
/// / diskPolicy land when a consumer needs them, together with their
/// backing fields on <see cref="RegionAttributes"/>.
/// </para>
/// </remarks>
internal sealed class RegionAttributesFactory
{
    private readonly RegionAttributes _attrs;

    /// <summary>Default-initialised attributes (cppcache <c>RegionAttributesFactory()</c>).</summary>
    public RegionAttributesFactory()
    {
        _attrs = new RegionAttributes();
    }

    /// <summary>Initialise from an existing snapshot (cppcache <c>explicit RegionAttributesFactory(const RegionAttributes)</c>).</summary>
    public RegionAttributesFactory(RegionAttributes seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _attrs = seed.Clone();
    }

    public RegionAttributesFactory SetPoolName(string poolName)
    {
        _attrs.PoolName = poolName;
        return this;
    }

    public RegionAttributesFactory SetInitialCapacity(int initialCapacity)
    {
        _attrs.InitialCapacity = initialCapacity;
        return this;
    }

    public RegionAttributesFactory SetLoadFactor(float loadFactor)
    {
        _attrs.LoadFactor = loadFactor;
        return this;
    }

    public RegionAttributesFactory SetConcurrencyLevel(int concurrencyLevel)
    {
        _attrs.ConcurrencyLevel = concurrencyLevel;
        return this;
    }

    public RegionAttributesFactory SetLruEntriesLimit(int entriesLimit)
    {
        _attrs.LruEntriesLimit = entriesLimit;
        return this;
    }

    public RegionAttributesFactory SetCachingEnabled(bool cachingEnabled)
    {
        _attrs.CachingEnabled = cachingEnabled;
        return this;
    }

    public RegionAttributesFactory SetCloningEnabled(bool cloningEnabled)
    {
        _attrs.CloningEnabled = cloningEnabled;
        return this;
    }

    public RegionAttributesFactory SetConcurrencyChecksEnabled(bool concurrencyChecksEnabled)
    {
        _attrs.ConcurrencyChecksEnabled = concurrencyChecksEnabled;
        return this;
    }

    public RegionAttributesFactory SetCacheLoader(ICacheLoader cacheLoader)
    {
        _attrs.CacheLoader = cacheLoader;
        return this;
    }

    public RegionAttributesFactory SetCacheWriter(ICacheWriter cacheWriter)
    {
        _attrs.CacheWriter = cacheWriter;
        return this;
    }

    public RegionAttributesFactory SetCacheListener(ICacheListener cacheListener)
    {
        _attrs.CacheListener = cacheListener;
        return this;
    }

    /// <summary>
    /// Set the overflow-to-disk backing store. cppcache
    /// <c>RegionAttributesFactory::setPersistenceManager</c> — 搭
    /// <see cref="SetDiskPolicy"/>(<c>Overflows</c>)才生效;create-time
    /// 接線仍 CUT(Phase 4)。
    /// </summary>
    public RegionAttributesFactory SetPersistenceManager(IPersistenceManager persistenceManager)
    {
        _attrs.PersistenceManager = persistenceManager;
        return this;
    }

    /// <summary>Set the LRU disk policy (<c>None</c> / <c>Overflows</c>). cppcache <c>RegionAttributesFactory::setDiskPolicy</c>.</summary>
    public RegionAttributesFactory SetDiskPolicy(CacheDiskPolicy diskPolicy)
    {
        _attrs.DiskPolicy = diskPolicy;
        return this;
    }

    /// <summary>
    /// Snapshot the current attribute set; mirrors cppcache
    /// <c>RegionAttributesFactory::create()</c> (return-by-value).
    /// </summary>
    public RegionAttributes Create() => _attrs.Clone();
}
