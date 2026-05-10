namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>region-attributes-type</c>. Every attribute is nullable
/// because the XSD defaults are unspecified — null means "fall back to
/// whatever cppcache decides".
/// </summary>
public class CacheXmlRegionAttributesOptions
{
    /// <summary><c>caching-enabled</c>.</summary>
    public bool? CachingEnabled { get; set; }

    /// <summary><c>cloning-enabled</c>.</summary>
    public bool? CloningEnabled { get; set; }

    /// <summary><c>scope</c>.</summary>
    public CacheXmlScope? Scope { get; set; }

    /// <summary><c>initial-capacity</c>.</summary>
    public int? InitialCapacity { get; set; }

    /// <summary><c>load-factor</c>.</summary>
    public float? LoadFactor { get; set; }

    /// <summary><c>concurrency-level</c>.</summary>
    public int? ConcurrencyLevel { get; set; }

    /// <summary><c>lru-entries-limit</c>.</summary>
    public int? LruEntriesLimit { get; set; }

    /// <summary><c>disk-policy</c>.</summary>
    public CacheXmlDiskPolicy? DiskPolicy { get; set; }

    /// <summary><c>endpoints</c>.</summary>
    public string Endpoints { get; set; } = string.Empty;

    /// <summary><c>client-notification</c>.</summary>
    public bool? ClientNotification { get; set; }

    /// <summary><c>pool-name</c> — references a
    /// <see cref="CacheXmlPoolOptions.Name"/> in
    /// <see cref="CacheXmlOptions.Pools"/>.</summary>
    public string PoolName { get; set; } = string.Empty;

    /// <summary><c>concurrency-checks-enabled</c>.</summary>
    public bool? ConcurrencyChecksEnabled { get; set; }

    /// <summary><c>id</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>refid</c>.</summary>
    public string RefId { get; set; } = string.Empty;

    /// <summary><c>&lt;region-time-to-live&gt;</c>.</summary>
    public CacheXmlExpirationOptions? RegionTimeToLive { get; set; }

    /// <summary><c>&lt;region-idle-time&gt;</c>.</summary>
    public CacheXmlExpirationOptions? RegionIdleTime { get; set; }

    /// <summary><c>&lt;entry-time-to-live&gt;</c>.</summary>
    public CacheXmlExpirationOptions? EntryTimeToLive { get; set; }

    /// <summary><c>&lt;entry-idle-time&gt;</c>.</summary>
    public CacheXmlExpirationOptions? EntryIdleTime { get; set; }

    /// <summary><c>&lt;partition-resolver&gt;</c>.</summary>
    public CacheXmlLibraryOptions? PartitionResolver { get; set; }

    /// <summary><c>&lt;cache-loader&gt;</c>.</summary>
    public CacheXmlLibraryOptions? CacheLoader { get; set; }

    /// <summary><c>&lt;cache-listener&gt;</c>.</summary>
    public CacheXmlLibraryOptions? CacheListener { get; set; }

    /// <summary><c>&lt;cache-writer&gt;</c>.</summary>
    public CacheXmlLibraryOptions? CacheWriter { get; set; }

    /// <summary><c>&lt;persistence-manager&gt;</c>.</summary>
    public CacheXmlPersistenceManagerOptions? PersistenceManager { get; set; }
}
