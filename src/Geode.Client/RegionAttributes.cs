namespace Geode.Client;

/// <summary>
/// Region configuration bag built by <see cref="RegionAttributesFactory"/>.
/// Snapshotted via <see cref="Clone"/> at <see cref="RegionAttributesFactory.Create"/>
/// time so further factory mutations don't affect already-built regions.
/// </summary>
/// <remarks>
/// Mirrors cppcache <c>RegionAttributes</c>
/// (<c>cppcache/include/geode/RegionAttributes.hpp</c> +
/// <c>cppcache/src/RegionAttributes.cpp:36-58</c>) 1:1. Phase 1.x carries
/// only the fields exposed through <see cref="RegionFactory"/> setters;
/// expiration / listener / persistence / partition-resolver fields land
/// in Phase 2+.
/// </remarks>
public sealed class RegionAttributes
{
    // RegionAttributes.cpp:50 — m_initialCapacity(10000)
    public int InitialCapacity { get; set; } = 10000;

    // RegionAttributes.cpp:51 — m_loadFactor(0.75)
    public float LoadFactor { get; set; } = 0.75f;

    // RegionAttributes.cpp:52 — m_concurrencyLevel(16)
    public int ConcurrencyLevel { get; set; } = 16;

    // RegionAttributes.cpp:43 — m_lruEntriesLimit(0)
    public int LruEntriesLimit { get; set; }

    // RegionAttributes.cpp:44 — m_caching(true)
    public bool CachingEnabled { get; set; } = true;

    // RegionAttributes.cpp:57 — m_isClonable(false)
    public bool CloningEnabled { get; set; }

    // RegionAttributes.cpp:58 — m_isConcurrencyChecksEnabled(true)
    public bool ConcurrencyChecksEnabled { get; set; } = true;

    // RegionAttributes.cpp:501 — m_poolName default empty string
    public string PoolName { get; set; } = string.Empty;

    /// <summary>
    /// Deep copy used by <see cref="RegionAttributesFactory.Create"/> so the
    /// region carries an immutable view independent of further setter calls.
    /// </summary>
    public RegionAttributes Clone()
    {
        return new RegionAttributes
        {
            InitialCapacity = InitialCapacity,
            LoadFactor = LoadFactor,
            ConcurrencyLevel = ConcurrencyLevel,
            LruEntriesLimit = LruEntriesLimit,
            CachingEnabled = CachingEnabled,
            CloningEnabled = CloningEnabled,
            ConcurrencyChecksEnabled = ConcurrencyChecksEnabled,
            PoolName = PoolName,
        };
    }
}
