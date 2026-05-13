namespace Geode.Client.Options;

/// <summary>
/// User-facing configuration for the Geode client. Bound from the
/// <c>"Geode"</c> section of <c>appsettings.json</c> via
/// <c>IOptions&lt;GeodeClientOptions&gt;</c> and consumed by the (Phase 5)
/// <c>AddGeodeClient(...)</c> DI extension.
/// </summary>
/// <remarks>
/// <para>
/// Property set is derived from cppcache <c>SystemProperties</c> (file
/// <c>cppcache/include/geode/SystemProperties.hpp</c> + defaults in
/// <c>cppcache/src/SystemProperties.cpp</c>). To make the audit
/// auditable we mirror <b>every</b> cppcache field for now; groups that
/// CLAUDE.md replaces (statistics → <c>EventCounters</c>, log →
/// <c>ILogger</c>) or marks out of MVP scope are still here so their
/// removal can be justified by "no consumer reads it" rather than by
/// memory. The deletion shortlist:
/// </para>
/// <list type="bullet">
///   <item><see cref="Statistics"/> — replaced by <c>EventCounters</c> / OpenTelemetry.</item>
///   <item><see cref="Log"/> — replaced by <c>ILogger</c> + filter levels.</item>
///   <item><see cref="Heap"/> — server-side concepts.</item>
///   <item><see cref="Tx"/> / <see cref="PoolOptions.BucketWaitTimeout"/> — out of MVP scope.</item>
///   <item><see cref="ThreadPoolSize"/> / <see cref="EnableChunkHandlerThread"/> — .NET ThreadPool managed.</item>
///   <item><see cref="Security"/> — DH credentials are deprecated upstream.</item>
///   <item><see cref="Pdx"/> — Phase 11.</item>
///   <item><see cref="CacheXmlFile"/> — CLAUDE.md cuts <c>cache.xml</c> entirely.</item>
/// </list>
/// <para>
/// The plan is to delete the unused groups before Phase 5 ships, once
/// the consuming code makes it obvious which fields are dead.
/// </para>
/// </remarks>
public class GeodeClientOptions
{
    /// <summary>
    /// Distributed-system / client name shown in server logs. Mirrors
    /// cppcache <c>name</c>. Default empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Path to a legacy <c>cache.xml</c> file. Mirrors cppcache
    /// <c>cache-xml-file</c>; default empty. CLAUDE.md cuts cache.xml
    /// entirely — included only to make its removal auditable.
    /// </summary>
    public string CacheXmlFile { get; set; } = string.Empty;

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
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public PoolOptions Pool { get; set; } = new();

    /// <summary>TLS / SSL settings. See <see cref="TlsOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public TlsOptions Tls { get; set; } = new();

    /// <summary>
    /// Subscription / durable-client / event-notification settings.
    /// See <see cref="SubscriptionOptions"/>.
    /// </summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public SubscriptionOptions Subscription { get; set; } = new();

    /// <summary>File-logging settings. See <see cref="LogOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public LogOptions Log { get; set; } = new();

    /// <summary>Statistics-archive settings. See <see cref="StatisticsOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public StatisticsOptions Statistics { get; set; } = new();

    /// <summary>Security / auth settings. See <see cref="SecurityOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>Transaction settings. See <see cref="TxOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public TxOptions Tx { get; set; } = new();

    /// <summary>Heap-LRU / tombstone settings. See <see cref="HeapOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public HeapOptions Heap { get; set; } = new();

    /// <summary>PDX-serialisation settings. See <see cref="PdxOptions"/>.</summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public PdxOptions Pdx { get; set; } = new();

    /// <summary>
    /// Wire-serialisation safety bounds (depth limit etc.). See
    /// <see cref="SerializationOptions"/>. No cppcache analogue —
    /// added independently to defend against malicious / pathological
    /// server payloads.
    /// </summary>
    /// <remarks>Settable so <see cref="DeepClone"/> can reassign — see <see cref="DeepClone"/>.</remarks>
    public SerializationOptions Serialization { get; set; } = new();

    /// <summary>
    /// Declarative <c>cache.xml</c> contents — named pools, region
    /// trees, PDX defaults. See <see cref="CacheXmlOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null when the caller did not supply cache.xml-style config</b>
    /// (the normal case &#x2014; we go through the programmatic /
    /// <see cref="PoolOptions"/> path equivalent to cppcache's path
    /// (b)). Non-null when a caller explicitly mirrors cppcache path
    /// (a) and provides declarative pool / region / PDX defaults.
    /// </para>
    /// <para>
    /// Distinct from <see cref="CacheXmlFile"/> (which is the path to
    /// the file). Both are deletion candidates if the path-(a) loader
    /// is never built.
    /// </para>
    /// </remarks>
    public CacheXmlOptions? CacheXml { get; set; }

    /// <summary>
    /// Deep clone the entire options tree. Each sub-options class
    /// implements its own <c>DeepClone()</c>; this method delegates so
    /// the clone is fully detached from <paramref name="this"/> (mutating
    /// the clone via <see cref="IGeodeCacheFactory.Create"/>'s
    /// <c>action</c> callback does not affect the registered config).
    /// </summary>
    public GeodeClientOptions DeepClone()
    {
        var clone = (GeodeClientOptions)MemberwiseClone();
        clone.Pool = Pool.DeepClone();
        clone.Tls = Tls.DeepClone();
        clone.Subscription = Subscription.DeepClone();
        clone.Log = Log.DeepClone();
        clone.Statistics = Statistics.DeepClone();
        clone.Security = Security.DeepClone();
        clone.Tx = Tx.DeepClone();
        clone.Heap = Heap.DeepClone();
        clone.Pdx = Pdx.DeepClone();
        clone.Serialization = Serialization.DeepClone();
        clone.CacheXml = CacheXml?.DeepClone();
        return clone;
    }

    /// <summary>
    /// Validate the entire options tree. Each sub-options class
    /// contributes its own failures, prefixed with its property path.
    /// The caller (typically <c>GeodeClientOptionsValidator</c> or
    /// <c>IGeodeCacheFactory.Create</c>) wraps the result in a
    /// <c>ValidateOptionsResult</c>.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        foreach (var f in Pool.Validate($"{prefix}.Pool")) yield return f;
        foreach (var f in Tls.Validate($"{prefix}.Tls")) yield return f;
        foreach (var f in Subscription.Validate($"{prefix}.Subscription")) yield return f;
        foreach (var f in Log.Validate($"{prefix}.Log")) yield return f;
        foreach (var f in Statistics.Validate($"{prefix}.Statistics")) yield return f;
        foreach (var f in Security.Validate($"{prefix}.Security")) yield return f;
        foreach (var f in Tx.Validate($"{prefix}.Tx")) yield return f;
        foreach (var f in Heap.Validate($"{prefix}.Heap")) yield return f;
        foreach (var f in Pdx.Validate($"{prefix}.Pdx")) yield return f;
        foreach (var f in Serialization.Validate($"{prefix}.Serialization")) yield return f;
        if (CacheXml is not null)
            foreach (var f in CacheXml.Validate($"{prefix}.CacheXml")) yield return f;
    }
}
