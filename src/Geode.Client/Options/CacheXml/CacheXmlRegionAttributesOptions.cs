namespace Geode.Client.Options;

/// <summary>
/// Mirrors <c>region-attributes-type</c>. Every attribute is nullable
/// because the XSD defaults are unspecified — null means "fall back to
/// whatever cppcache decides".
/// </summary>
public class CacheXmlRegionAttributesOptions : ICloneable
{
    public CacheXmlRegionAttributesOptions() { }

    public CacheXmlRegionAttributesOptions(CacheXmlRegionAttributesOptions other)
    {
        CachingEnabled = other.CachingEnabled;
        CloningEnabled = other.CloningEnabled;
        Scope = other.Scope;
        InitialCapacity = other.InitialCapacity;
        LoadFactor = other.LoadFactor;
        ConcurrencyLevel = other.ConcurrencyLevel;
        LruEntriesLimit = other.LruEntriesLimit;
        DiskPolicy = other.DiskPolicy;
        Endpoints = other.Endpoints;
        ClientNotification = other.ClientNotification;
        PoolName = other.PoolName;
        ConcurrencyChecksEnabled = other.ConcurrencyChecksEnabled;
        RefId = other.RefId;
        RegionTimeToLive = other.RegionTimeToLive?.Clone();
        RegionIdleTime = other.RegionIdleTime?.Clone();
        EntryTimeToLive = other.EntryTimeToLive?.Clone();
        EntryIdleTime = other.EntryIdleTime?.Clone();
        // Virtual Clone() on CacheXmlLibraryOptions dispatches to the
        // runtime subtype (e.g. CacheXmlPersistenceManagerOptions),
        // so polymorphism is preserved without a cast.
        PartitionResolver = other.PartitionResolver?.Clone();
        CacheLoader = other.CacheLoader?.Clone();
        CacheListener = other.CacheListener?.Clone();
        CacheWriter = other.CacheWriter?.Clone();
        PersistenceManager = other.PersistenceManager?.Clone();
    }

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

    /// <summary>
    /// Inner <c>&lt;region-attributes refid="..."&gt;</c> reference.
    /// Mirrors the cppcache schema; currently ignored — refid resolution
    /// only honours the outer <see cref="CacheXmlRegionOptions.RefId"/>.
    /// Wire this in when a consumer actually needs inner-element refid.
    /// </summary>
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

    /// <summary>Deep clone via copy constructor.</summary>
    public CacheXmlRegionAttributesOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate. Delegates to non-null nested options; this class has no own structural rules.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (RegionTimeToLive is not null)
            foreach (var f in RegionTimeToLive.Validate($"{prefix}.RegionTimeToLive")) yield return f;
        if (RegionIdleTime is not null)
            foreach (var f in RegionIdleTime.Validate($"{prefix}.RegionIdleTime")) yield return f;
        if (EntryTimeToLive is not null)
            foreach (var f in EntryTimeToLive.Validate($"{prefix}.EntryTimeToLive")) yield return f;
        if (EntryIdleTime is not null)
            foreach (var f in EntryIdleTime.Validate($"{prefix}.EntryIdleTime")) yield return f;
        if (PartitionResolver is not null)
            foreach (var f in PartitionResolver.Validate($"{prefix}.PartitionResolver")) yield return f;
        if (CacheLoader is not null)
            foreach (var f in CacheLoader.Validate($"{prefix}.CacheLoader")) yield return f;
        if (CacheListener is not null)
            foreach (var f in CacheListener.Validate($"{prefix}.CacheListener")) yield return f;
        if (CacheWriter is not null)
            foreach (var f in CacheWriter.Validate($"{prefix}.CacheWriter")) yield return f;
        if (PersistenceManager is not null)
            foreach (var f in PersistenceManager.Validate($"{prefix}.PersistenceManager")) yield return f;
    }
}
