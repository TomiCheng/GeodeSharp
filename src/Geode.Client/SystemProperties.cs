using Microsoft.Extensions.Logging;

namespace Geode.Client;

/// <summary>
/// Per-cache settings snapshot — drives one <see cref="IGeodeCache"/> instance.
/// </summary>
/// <remarks>
/// All properties expose a public setter so the host can populate / override
/// them, typically inside <see cref="IGeodeCacheFactory.CreateAsync"/>'s
/// <c>configure</c> callback. The <c>Geode.Client.Extensions.Hosting</c>
/// package binds an <c>IConfiguration</c> section into this snapshot;
/// callers without that package construct it directly.
/// </remarks>
public sealed class SystemProperties
{
    /// <summary>Statistics sampling interval. Default: 1 second.</summary>
    public TimeSpan StatisticsSampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Whether statistics sampling is enabled. Default: <see langword="false"/>.</summary>
    public bool StatisticsEnabled { get; set; }

    /// <summary>Statistics archive file path. Default: <c>statArchive.gfs</c>.</summary>
    public string StatisticsArchiveFile { get; set; } = "statArchive.gfs";

    /// <summary>Log file path; empty routes to stdout. Default: empty.</summary>
    public string LogFilename { get; set; } = string.Empty;

    /// <summary>Minimum log severity emitted. Default: <see cref="LogLevel.Information"/>.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>Disable shuffling of the endpoint list on pool startup. Default: <see langword="false"/>.</summary>
    public bool DisableShufflingEndpoint { get; set; }

    /// <summary>Cache instance name. Default: empty.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Legacy field; <c>cache.xml</c> is not used by this client. Default: empty.</summary>
    public string CacheXMLFile { get; set; } = string.Empty;

    /// <summary>Single log-file size limit in bytes; <c>0</c> means unlimited. Default: <c>0</c>.</summary>
    public uint LogFileSizeLimit { get; set; }

    /// <summary>Total log-disk space limit in bytes; <c>0</c> means unlimited. Default: <c>0</c>.</summary>
    public uint LogDiskSpaceLimit { get; set; }

    /// <summary>Single statistics-file size limit in bytes; <c>0</c> means unlimited. Default: <c>0</c>.</summary>
    public uint StatsFileSizeLimit { get; set; }

    /// <summary>Total statistics-disk space limit in bytes; <c>0</c> means unlimited. Default: <c>0</c>.</summary>
    public uint StatsDiskSpaceLimit { get; set; }

    /// <summary>Default number of connections per pool. Default: <c>5</c>.</summary>
    public uint ConnectionPoolSize { get; set; } = 5;

    /// <summary>
    /// Heap-LRU eviction trigger in <b>MB</b>; <c>0</c> disables heap-LRU. Default: <c>0</c>.
    /// </summary>
    public long HeapLRULimit { get; set; }

    /// <summary>
    /// Process-wide heap-LRU on/off flag; <see langword="true"/> when <see cref="HeapLRULimit"/> &gt; <c>0</c>.
    /// </summary>
    public bool HeapLRULimitEnabled => HeapLRULimit > 0;

    /// <summary>
    /// Heap-LRU eviction step, in percent of current heap. Default: <c>10</c>.
    /// </summary>
    public int HeapLRUDelta { get; set; } = 10;

    /// <summary>Socket buffer size in bytes (send and receive). Default: <c>66560</c> (65 KB).</summary>
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    /// <summary>Interval between liveness pings to each server. Default: 10 seconds.</summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Interval at which subscription redundancy is checked and restored. Default: 10 seconds.</summary>
    public TimeSpan RedundancyMonitorInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Interval for acknowledging received subscription messages. Default: 1 second.</summary>
    public TimeSpan NotifyAckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>TTL for the duplicate-notification suppression cache. Default: 300 seconds.</summary>
    public TimeSpan NotifyDupCheckLife { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>Free-form security property bag passed to the server during handshake. Default: empty.</summary>
    public IReadOnlyDictionary<string, string> SecurityProperties { get; set; } =
        new Dictionary<string, string>();

    /// <summary>Diffie-Hellman algorithm for legacy client encryption (deprecated). Default: empty.</summary>
    public string SecurityClientDhAlgo { get; set; } = string.Empty;

    /// <summary>Path to the legacy client keystore for DH-based encryption. Default: empty.</summary>
    public string SecurityClientKsPath { get; set; } = string.Empty;

    /// <summary>Durable-client identifier; empty disables durable mode. Default: empty.</summary>
    public string DurableClientId { get; set; } = string.Empty;

    /// <summary>Server-side queue retention for a disconnected durable client. Default: 300 seconds.</summary>
    public TimeSpan DurableTimeout { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>TCP connect timeout per server. Default: 59 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(59);

    /// <summary>Linux-only post-connect wait window; <see cref="TimeSpan.Zero"/> disables. Default: zero.</summary>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Wait window for partition-bucket resolution; <see cref="TimeSpan.Zero"/> disables. Default: zero.</summary>
    public TimeSpan BucketWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Whether the cache auto-signals readiness for server events on init. Default: <see langword="true"/>.</summary>
    public bool AutoReadyForEvents { get; set; } = true;

    /// <summary>Whether SSL/TLS is enabled for all server connections. Default: <see langword="false"/>.</summary>
    public bool SslEnabled { get; set; }

    /// <summary>Whether per-operation time statistics are recorded. Default: <see langword="false"/>.</summary>
    public bool TimeStatisticsEnabled { get; set; }

    /// <summary>Path to the client SSL keystore. Default: empty.</summary>
    public string SslKeyStore { get; set; } = string.Empty;

    /// <summary>Path to the client SSL truststore. Default: empty.</summary>
    public string SslTrustStore { get; set; } = string.Empty;

    /// <summary>Password for the client SSL keystore. Default: empty.</summary>
    public string SslKeystorePassword { get; set; } = string.Empty;

    /// <summary>Notification-event conflation mode (<c>server</c> / <c>true</c> / <c>false</c>). Default: <c>server</c>.</summary>
    public string ConflateEvents { get; set; } = "server";

    /// <summary>Worker thread pool size. Default: <see cref="Environment.ProcessorCount"/> × 2.</summary>
    public uint ThreadPoolSize { get; set; } = (uint)(Environment.ProcessorCount * 2);

    /// <summary>TTL for suspended transactions. Default: 30 seconds.</summary>
    public TimeSpan SuspendedTxTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>TTL for tombstones (deleted-entry markers). Default: 480 seconds.</summary>
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromSeconds(480);

    /// <summary>Whether to dispatch chunk-handling onto a dedicated thread. Default: <see langword="false"/>.</summary>
    public bool EnableChunkHandlerThread { get; set; }

    /// <summary>Whether to clear the PDX type-id cache on client disconnect. Default: <see langword="false"/>.</summary>
    public bool OnClientDisconnectClearPdxTypeIds { get; set; }

    // ── Serialization safety caps ────────────────────────────────────
    //
    // Defends against hostile / buggy wire payloads claiming gigabyte-scale
    // lengths. Tuning is the host's call: raise for legitimate bulk-data
    // workloads, lower for tighter sandboxing. Validated at host build time
    // by the Hosting package's options validator.

    /// <summary>
    /// Maximum nested-container depth on encode / decode. Default <b>64</b>
    /// (matches <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>).
    /// Must be <c>&gt;= 1</c>.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Maximum element count for any length-prefixed wire payload
    /// (primitive arrays, <c>CacheableObjectArray</c>, collections).
    /// Default <b>1,000,000</b>. Inclusive bound; <c>0</c> allows empty
    /// payloads only; negative rejected at host build time.
    /// </summary>
    /// <remarks>
    /// Threat: a hostile server can pin the i32 length prefix at
    /// <see cref="int.MaxValue"/>, forcing the client to pre-allocate
    /// multi-GB before reading. Capping here yields a fast
    /// <c>GeodeException</c> instead. Write side applies the same cap
    /// (caller-bug guard) — encode-side breach throws
    /// <see cref="InvalidOperationException"/>.
    /// </remarks>
    public int MaxArrayLength { get; set; } = 1_000_000;

    /// <summary>
    /// Maximum byte count for a single <see cref="byte"/><c>[]</c>
    /// payload (<c>DSCode.CacheableBytes</c> = 46). Default <b>10 MB</b>.
    /// Split from <see cref="MaxArrayLength"/> because byte[] is the
    /// canonical "binary blob" type (file content, encrypted payloads)
    /// with a different natural size distribution. Same threat model
    /// and check semantics.
    /// </summary>
    public int MaxBytesLength { get; set; } = 10_000_000;

    /// <summary>
    /// Maximum length for any single <c>CacheableString</c> wire variant
    /// (DSCodes 42 / 87 / 88 / 89). Unit is whatever the variant's length
    /// prefix counts (modified-UTF-8 bytes for 42; chars for the others).
    /// Default <b>1,000,000</b>. Same threat model as
    /// <see cref="MaxArrayLength"/>; tuned separately because string
    /// payloads have a different natural size distribution from bulk arrays.
    /// </summary>
    public int MaxStringLength { get; set; } = 1_000_000;

  }
