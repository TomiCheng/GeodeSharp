using Geode.Client.Internal;
using Geode.Client.Options;

namespace Geode.Client;

/// <summary>
/// Region configuration bag built by <see cref="RegionAttributesFactory"/>.
/// </summary>
public sealed class RegionAttributes : ICloneable
{
    // ── Map attributes (cppcache RegionAttributes.cpp:50-53) ─────

    /// <summary>Initial bucket count of the local entry map.</summary>
    public int InitialCapacity { get; set; } = 10000;

    /// <summary>Load factor of the local entry map.</summary>
    public float LoadFactor { get; set; } = 0.75f;

    /// <summary>Estimated concurrent-writer count sizing the local entry map's striping.</summary>
    public int ConcurrencyLevel { get; set; } = 16;

    /// <summary>Max local entries before LRU eviction; <c>0</c> disables LRU.</summary>
    public int LruEntriesLimit { get; set; }

    /// <summary>What happens to entries past the LRU limit (none / overflow-to-disk).</summary>
    public CacheDiskPolicy DiskPolicy { get; set; } = CacheDiskPolicy.None;

    /// <summary>Action applied to an LRU-evicted entry (local-destroy / invalidate).</summary>
    public CacheExpirationAction LruEvictionAction { get; set; } = CacheExpirationAction.LocalDestroy;

    // ── Caching / cloning / concurrency ──────────────────────────

    /// <summary>Whether entries are stored locally; <see langword="false"/> routes every op to the server.</summary>
    public bool CachingEnabled { get; set; } = true;

    /// <summary>Whether to clone the old value before applying a delta.</summary>
    public bool CloningEnabled { get; set; }

    /// <summary>Whether per-entry version checks run on region entries.</summary>
    public bool ConcurrencyChecksEnabled { get; set; } = true;

    // ── Expiration ───────────────────────────────────────────────
    // 4 (duration, action) pairs. Phase 2+ expiration phase wires
    // the regional / per-entry timers; today they're carried so
    // EntriesMapFactory's ttl/idle branch can already read them.

    /// <summary>Region-wide time-to-live; <see cref="TimeSpan.Zero"/> disables.</summary>
    public TimeSpan RegionTimeToLive { get; set; } = TimeSpan.Zero;

    /// <summary>Action applied when the region's time-to-live expires.</summary>
    public CacheExpirationAction RegionTimeToLiveAction { get; set; } = CacheExpirationAction.Invalidate;

    /// <summary>Region-wide idle timeout; <see cref="TimeSpan.Zero"/> disables.</summary>
    public TimeSpan RegionIdleTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Action applied when the region's idle timeout expires.</summary>
    public CacheExpirationAction RegionIdleTimeoutAction { get; set; } = CacheExpirationAction.Invalidate;

    /// <summary>Per-entry time-to-live; <see cref="TimeSpan.Zero"/> disables.</summary>
    public TimeSpan EntryTimeToLive { get; set; } = TimeSpan.Zero;

    /// <summary>Action applied when an entry's time-to-live expires.</summary>
    public CacheExpirationAction EntryTimeToLiveAction { get; set; } = CacheExpirationAction.Invalidate;

    /// <summary>Per-entry idle timeout; <see cref="TimeSpan.Zero"/> disables.</summary>
    public TimeSpan EntryIdleTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Action applied when an entry's idle timeout expires.</summary>
    public CacheExpirationAction EntryIdleTimeoutAction { get; set; } = CacheExpirationAction.Invalidate;

    // ── Callback objects ─────────────────────────────────────────
    // No factory setter wired yet (Phase 2+ / Phase 4).

    /// <summary>Loader invoked on a cache miss, or <see langword="null"/> for none.</summary>
    public ICacheLoader? CacheLoader { get; set; }

    /// <summary>Writer consulted before a mutating op (may veto), or <see langword="null"/> for none.</summary>
    public ICacheWriter? CacheWriter { get; set; }

    /// <summary>Listener notified after cache events, or <see langword="null"/> for none.</summary>
    public ICacheListener? CacheListener { get; set; }

    /// <summary>Resolver mapping entries to partition buckets for single-hop routing, or <see langword="null"/> for none.</summary>
    public IPartitionResolver? PartitionResolver { get; set; }

    // ── Pool ─────────────────────────────────────────────────────

    /// <summary>Name of the pool the region attaches to; empty falls back to the cache's default pool.</summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary>Whether server→client notification (subscription / register-interest) is enabled for the region.</summary>
    /// <remarks>No factory setter — set internally / forced on for HA regions, not via the user attributes factory. Phase 4+ (subscription / HA channel); no reader until then.</remarks>
    public bool ClientNotificationEnabled { get; set; }

    /// <summary>Whether entry-level expiry is configured (TTL or idle-timeout &gt; 0).</summary>
    public bool EntryExpiryEnabled => EntryTimeToLive > TimeSpan.Zero || EntryIdleTimeout > TimeSpan.Zero;

    /// <summary>Whether region-level expiry is configured (TTL or idle-timeout &gt; 0).</summary>
    public bool RegionExpiryEnabled => RegionTimeToLive > TimeSpan.Zero || RegionIdleTimeout > TimeSpan.Zero;

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
            ClientNotificationEnabled = ClientNotificationEnabled,
        };
    }

    /// <summary>Explicit <see cref="ICloneable"/> implementation delegating to the strongly-typed <see cref="Clone"/>.</summary>
    object ICloneable.Clone() => Clone();
}
