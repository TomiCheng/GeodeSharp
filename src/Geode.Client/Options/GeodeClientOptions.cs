/*
namespace Geode.Client.Options;

/// <summary>
/// User-facing configuration for the Geode client. Bound from the
/// <c>"Geode"</c> section of <c>appsettings.json</c> via
/// <c>IOptions&lt;GeodeClientOptions&gt;</c> and consumed by
/// <c>AddGeodeClient(...)</c>.
/// </summary>
public class GeodeClientOptions: ICloneable
{
    /// <summary>
    /// Distributed-system / client name shown in server logs. Mirrors
    /// cppcache <c>name</c>. Default empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Worker-thread count for cppcache's internal dispatcher. Mirrors
    /// cppcache <c>max-fe-threads</c>; default
    /// <c>Environment.ProcessorCount * 2</c>. .NET uses
    /// <c>System.IO.Pipelines</c> + <c>ThreadPool</c>, so this is very
    /// likely a no-op.
    /// </summary>
    public uint ThreadPoolSize { get; set; } = (uint)(Environment.ProcessorCount * 2);

    /// <summary>
    /// Whether to dedicate a thread to chunked-response handling.
    /// Mirrors cppcache <c>enable-chunk-handler-thread</c>; default
    /// <c>false</c>. Almost certainly redundant under .NET's async I/O
    /// model.
    /// </summary>
    public bool EnableChunkHandlerThread { get; set; }

    /// <summary>Connection-pool tuning. See <see cref="PoolOptions"/>.</summary>
    public PoolOptions Pool { get; set; } = new();

    /// <summary>TLS / SSL settings. See <see cref="TlsOptions"/>.</summary>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>
    /// Subscription / durable-client / event-notification settings.
    /// See <see cref="SubscriptionOptions"/>.
    /// </summary>
    public SubscriptionOptions Subscription { get; set; } = new();

    /// <summary>Security / auth settings. See <see cref="SecurityOptions"/>.</summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>Transaction settings. See <see cref="TxOptions"/>.</summary>
    public TxOptions Tx { get; set; } = new();

    /// <summary>Heap-LRU / tombstone settings. See <see cref="HeapOptions"/>.</summary>
    public HeapOptions Heap { get; set; } = new();

    /// <summary>PDX-serialisation settings. See <see cref="PdxOptions"/>.</summary>
    public PdxOptions Pdx { get; set; } = new();

    /// <summary>
    /// Wire-serialisation safety bounds (depth limit etc.). See
    /// <see cref="SerializationOptions"/>. No cppcache analogue —
    /// added independently to defend against malicious / pathological
    /// server payloads.
    /// </summary>
    public SerializationOptions Serialization { get; set; } = new();

    /// <summary>
    /// Declarative <c>cache.xml</c> contents — named pools, region
    /// trees, PDX defaults. Null when the caller uses the programmatic
    /// <see cref="PoolOptions"/> path (the normal case).
    /// </summary>
    public CacheOptions? Cache { get; set; }

    public GeodeClientOptions() { }

    public GeodeClientOptions(GeodeClientOptions other)
    {
        Name = other.Name;
        ThreadPoolSize = other.ThreadPoolSize;
        EnableChunkHandlerThread = other.EnableChunkHandlerThread;
        Pool = other.Pool.Clone();
        Tls = other.Tls.Clone();
        Subscription = other.Subscription.Clone();
        Security = other.Security.Clone();
        Tx = other.Tx.Clone();
        Heap = other.Heap.Clone();
        Pdx = other.Pdx.Clone();
        Serialization = other.Serialization.Clone();
        Cache = other.Cache?.Clone();
    }

    /// <summary>Deep clone via copy constructor.</summary>
    public GeodeClientOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate the tree, recursing into each sub-options group.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        foreach (var f in Pool.Validate($"{prefix}.Pool")) yield return f;
        foreach (var f in Tls.Validate($"{prefix}.Tls")) yield return f;
        foreach (var f in Subscription.Validate($"{prefix}.Subscription")) yield return f;
        foreach (var f in Security.Validate($"{prefix}.Security")) yield return f;
        foreach (var f in Tx.Validate($"{prefix}.Tx")) yield return f;
        foreach (var f in Heap.Validate($"{prefix}.Heap")) yield return f;
        foreach (var f in Pdx.Validate($"{prefix}.Pdx")) yield return f;
        foreach (var f in Serialization.Validate($"{prefix}.Serialization")) yield return f;
        if (Cache is not null)
            foreach (var f in Cache.Validate($"{prefix}.Cache")) yield return f;
    }
}

*/