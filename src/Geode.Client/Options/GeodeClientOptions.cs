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
    public PoolOptions Pool { get; } = new();

    /// <summary>TLS / SSL settings. See <see cref="TlsOptions"/>.</summary>
    public TlsOptions Tls { get; } = new();

    /// <summary>
    /// Subscription / durable-client / event-notification settings.
    /// See <see cref="SubscriptionOptions"/>.
    /// </summary>
    public SubscriptionOptions Subscription { get; } = new();

    /// <summary>File-logging settings. See <see cref="LogOptions"/>.</summary>
    public LogOptions Log { get; } = new();

    /// <summary>Statistics-archive settings. See <see cref="StatisticsOptions"/>.</summary>
    public StatisticsOptions Statistics { get; } = new();

    /// <summary>Security / auth settings. See <see cref="SecurityOptions"/>.</summary>
    public SecurityOptions Security { get; } = new();

    /// <summary>Transaction settings. See <see cref="TxOptions"/>.</summary>
    public TxOptions Tx { get; } = new();

    /// <summary>Heap-LRU / tombstone settings. See <see cref="HeapOptions"/>.</summary>
    public HeapOptions Heap { get; } = new();

    /// <summary>PDX-serialisation settings. See <see cref="PdxOptions"/>.</summary>
    public PdxOptions Pdx { get; } = new();

    /// <summary>
    /// Wire-serialisation safety bounds (depth limit etc.). See
    /// <see cref="SerializationOptions"/>. No cppcache analogue —
    /// added independently to defend against malicious / pathological
    /// server payloads.
    /// </summary>
    public SerializationOptions Serialization { get; } = new();

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
}
