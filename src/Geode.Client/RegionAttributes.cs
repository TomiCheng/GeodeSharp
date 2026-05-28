using Geode.Client.Internal;
using Geode.Client.Options;

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
/// in Phase 2+ — they're declared now so feature ports can grep for the
/// cppcache name and find the C# counterpart at the same shape.
/// </remarks>
public sealed class RegionAttributes
{
    // ── Map attributes (cppcache RegionAttributes.cpp:50-53) ─────

    // RegionAttributes.cpp:50 — m_initialCapacity(10000)
    public int InitialCapacity { get; set; } = 10000;

    // RegionAttributes.cpp:51 — m_loadFactor(0.75)
    public float LoadFactor { get; set; } = 0.75f;

    // RegionAttributes.cpp:52 — m_concurrencyLevel(16)
    public int ConcurrencyLevel { get; set; } = 16;

    // RegionAttributes.cpp:43 — m_lruEntriesLimit(0)
    public int LruEntriesLimit { get; set; }

    // RegionAttributes.cpp:53 — m_diskPolicy(DiskPolicyType::NONE)
    public CacheDiskPolicy DiskPolicy { get; set; } = CacheDiskPolicy.None;

    // RegionAttributes.cpp:42 — m_lruEvictionAction(ExpirationAction::LOCAL_DESTROY)
    public CacheExpirationAction LruEvictionAction { get; set; } = CacheExpirationAction.LocalDestroy;

    // ── Caching / cloning / concurrency (cppcache .cpp:44, 57-58) ─

    // RegionAttributes.cpp:44 — m_caching(true)
    public bool CachingEnabled { get; set; } = true;

    // RegionAttributes.cpp:57 — m_isClonable(false)
    public bool CloningEnabled { get; set; }

    // RegionAttributes.cpp:58 — m_isConcurrencyChecksEnabled(true)
    public bool ConcurrencyChecksEnabled { get; set; } = true;

    // ── Expiration (cppcache .cpp:38-41, 46-49) ─────────────────
    // 4 (duration, action) pairs. Phase 2+ expiration phase wires
    // the regional / per-entry timers; today they're carried so
    // EntriesMapFactory's ttl/idle branch can already read them.

    // RegionAttributes.cpp:49 — m_regionTimeToLive(0s)
    public TimeSpan RegionTimeToLive { get; set; } = TimeSpan.Zero;

    // RegionAttributes.cpp:38 — m_regionTimeToLiveExpirationAction(INVALIDATE)
    public CacheExpirationAction RegionTimeToLiveAction { get; set; } = CacheExpirationAction.Invalidate;

    // RegionAttributes.cpp:48 — m_regionIdleTimeout(0s)
    public TimeSpan RegionIdleTimeout { get; set; } = TimeSpan.Zero;

    // RegionAttributes.cpp:39 — m_regionIdleTimeoutExpirationAction(INVALIDATE)
    public CacheExpirationAction RegionIdleTimeoutAction { get; set; } = CacheExpirationAction.Invalidate;

    // RegionAttributes.cpp:47 — m_entryTimeToLive(0s)
    public TimeSpan EntryTimeToLive { get; set; } = TimeSpan.Zero;

    // RegionAttributes.cpp:40 — m_entryTimeToLiveExpirationAction(INVALIDATE)
    public CacheExpirationAction EntryTimeToLiveAction { get; set; } = CacheExpirationAction.Invalidate;

    // RegionAttributes.cpp:46 — m_entryIdleTimeout(0s)
    public TimeSpan EntryIdleTimeout { get; set; } = TimeSpan.Zero;

    // RegionAttributes.cpp:41 — m_entryIdleTimeoutExpirationAction(INVALIDATE)
    public CacheExpirationAction EntryIdleTimeoutAction { get; set; } = CacheExpirationAction.Invalidate;

    // ── Callback objects (cppcache header getCacheLoader / etc.) ─
    // Parked as object? until each callback interface ships
    // (Phase 2+: ICacheLoader / ICacheWriter / ICacheListener;
    // Phase 4: IPartitionResolver for partitioned regions). cppcache
    // also carries Library + Factory string pairs for cache.xml dynamic
    // loading; not ported — .NET goes through DI / Type registration.

    // RegionAttributes header:87 — getCacheLoader() → shared_ptr<CacheLoader>
    public object? CacheLoader { get; set; }

    // RegionAttributes header:94 — getCacheWriter() → shared_ptr<CacheWriter>
    public object? CacheWriter { get; set; }

    // RegionAttributes header:101 — getCacheListener() → shared_ptr<CacheListener>
    public object? CacheListener { get; set; }

    // RegionAttributes header:109 — getPartitionResolver() → shared_ptr<PartitionResolver>
    public object? PartitionResolver { get; set; }

    // ── Pool ─────────────────────────────────────────────────────

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
            DiskPolicy = DiskPolicy,
            LruEvictionAction = LruEvictionAction,
            CachingEnabled = CachingEnabled,
            CloningEnabled = CloningEnabled,
            ConcurrencyChecksEnabled = ConcurrencyChecksEnabled,
            RegionTimeToLive = RegionTimeToLive,
            RegionTimeToLiveAction = RegionTimeToLiveAction,
            RegionIdleTimeout = RegionIdleTimeout,
            RegionIdleTimeoutAction = RegionIdleTimeoutAction,
            EntryTimeToLive = EntryTimeToLive,
            EntryTimeToLiveAction = EntryTimeToLiveAction,
            EntryIdleTimeout = EntryIdleTimeout,
            EntryIdleTimeoutAction = EntryIdleTimeoutAction,
            CacheLoader = CacheLoader,
            CacheWriter = CacheWriter,
            CacheListener = CacheListener,
            PartitionResolver = PartitionResolver,
            PoolName = PoolName,
        };
    }
}
