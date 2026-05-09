namespace Geode.Client.Options;

/// <summary>
/// <c>region-attributes/scope</c> enumeration. Source:
/// <c>cpp-cache-1.0.xsd</c>.
/// </summary>
public enum CacheXmlScope
{
    Local,
    DistributedNoAck,
    DistributedAck,
}

/// <summary>
/// <c>region-attributes/disk-policy</c> enumeration.
/// </summary>
public enum CacheXmlDiskPolicy
{
    None,
    Overflows,
    Persist,
}

/// <summary>
/// <c>expiration-attributes/action</c> enumeration.
/// </summary>
public enum CacheXmlExpirationAction
{
    Invalidate,
    Destroy,
    LocalInvalidate,
    LocalDestroy,
}

/// <summary>
/// Mirrors <c>&lt;expiration-attributes&gt;</c>. Used by the four
/// expiration slots on a region (entry-/region- × idle-time/ttl).
/// </summary>
public class CacheXmlExpirationOptions
{
    /// <summary><c>timeout</c> attribute (required).</summary>
    public TimeSpan Timeout { get; set; }

    /// <summary><c>action</c> attribute (optional).</summary>
    public CacheXmlExpirationAction? Action { get; set; }
}

/// <summary>
/// Mirrors <c>library-type</c> in the XSD —
/// <c>&lt;cache-loader&gt;</c>, <c>&lt;cache-listener&gt;</c>,
/// <c>&lt;cache-writer&gt;</c>, <c>&lt;partition-resolver&gt;</c>.
/// </summary>
/// <remarks>
/// These pointers reference a native shared library + entry function
/// used by cppcache to construct the callback. On the .NET side this
/// translates to a delegate / DI-registered type; the field is kept
/// here for parity only and is unlikely to ship in the .NET API.
/// </remarks>
public class CacheXmlLibraryOptions
{
    /// <summary><c>library-name</c> attribute (optional).</summary>
    public string LibraryName { get; set; } = string.Empty;

    /// <summary><c>library-function-name</c> attribute (required).</summary>
    public string LibraryFunctionName { get; set; } = string.Empty;
}

/// <summary>
/// Mirrors <c>&lt;persistence-manager&gt;</c>. Extends
/// <see cref="CacheXmlLibraryOptions"/> with a free-form
/// <c>&lt;properties&gt;&lt;property name= value=&gt;</c> bag.
/// </summary>
public class CacheXmlPersistenceManagerOptions : CacheXmlLibraryOptions
{
    /// <summary>
    /// Nested <c>&lt;property name="..." value="..."/&gt;</c> entries.
    /// </summary>
    public Dictionary<string, string> Properties { get; } = new();
}

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

/// <summary>
/// Mirrors <c>region-type</c>. Regions can nest via
/// <see cref="ChildRegions"/>.
/// </summary>
public class CacheXmlRegionOptions
{
    /// <summary><c>name</c> attribute (required).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>refid</c> attribute (optional) — copy attributes
    /// from a previously-defined region.</summary>
    public string RefId { get; set; } = string.Empty;

    /// <summary><c>&lt;region-attributes&gt;</c> child.</summary>
    public CacheXmlRegionAttributesOptions Attributes { get; } = new();

    /// <summary>Nested <c>&lt;region&gt;</c> children.</summary>
    public List<CacheXmlRegionOptions> ChildRegions { get; } = new();
}
