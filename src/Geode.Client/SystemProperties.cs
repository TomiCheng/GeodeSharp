using Microsoft.Extensions.Logging;

namespace Geode.Client;

/// <summary>
/// Per-cache settings snapshot — drives one <see cref="IGeodeCache"/>
/// instance. Mirror of cppcache <c>SystemProperties</c>
/// (<c>cppcache/include/geode/SystemProperties.hpp</c>); defaults match the
/// <c>Default*</c> constants in <c>cppcache/src/SystemProperties.cpp:78-126</c>.
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
    // cppcache SystemProperties.cpp:87 — DefaultSamplingInterval = 1s
    public TimeSpan StatisticsSampleInterval { get; set; } = TimeSpan.FromSeconds(1);

    // cppcache SystemProperties.cpp:88 — DefaultSamplingEnabled = false
    public bool StatisticsEnabled { get; set; }

    // cppcache SystemProperties.cpp:90 — DefaultStatArchive = "statArchive.gfs"
    public string StatisticsArchiveFile { get; set; } = "statArchive.gfs";

    // cppcache SystemProperties.cpp:91 — DefaultLogFilename = "" (stdout)
    public string LogFilename { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:93-94 — DefaultLogLevel = LogLevel::Config.
    // .NET LogLevel has no "Config"; Information is the closest level.
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    // cppcache SystemProperties.cpp:144 — m_disableShufflingEndpoint(false)
    public bool DisableShufflingEndpoint { get; set; }

    // cppcache SystemProperties.hpp:233 — DefaultName = ""
    public string Name { get; set; } = string.Empty;

    // cppcache SystemProperties.hpp:235 — DefaultCacheXMLFile = ""
    public string CacheXMLFile { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:107 — DefaultLogFileSizeLimit = 0 (unlimited)
    public uint LogFileSizeLimit { get; set; }

    // cppcache SystemProperties.cpp:108 — DefaultLogDiskSpaceLimit = 0 (unlimited)
    public uint LogDiskSpaceLimit { get; set; }

    // cppcache SystemProperties.cpp:109 — DefaultStatsFileSizeLimit = 0 (unlimited)
    public uint StatsFileSizeLimit { get; set; }

    // cppcache SystemProperties.cpp:110 — DefaultStatsDiskSpaceLimit = 0 (unlimited)
    public uint StatsDiskSpaceLimit { get; set; }

    // cppcache SystemProperties.cpp:96 — DefaultConnectionPoolSize = 5
    public uint ConnectionPoolSize { get; set; } = 5;

    // cppcache SystemProperties.cpp:112 — DefaultHeapLRULimit = 0 (disabled)
    public long HeapLRULimit { get; set; }

    /// <summary>
    /// Process-wide heap-LRU on/off flag. Mirrors cppcache
    /// <c>SystemProperties::heapLRULimitEnabled()</c>
    /// (<c>cppcache/include/geode/SystemProperties.hpp:143</c>):
    /// <c>m_heapLRULimit &gt; 0</c>.
    /// </summary>
    public bool HeapLRULimitEnabled => HeapLRULimit > 0;

    // cppcache SystemProperties.cpp:113 — DefaultHeapLRUDelta = 10 (% eviction step)
    public int HeapLRUDelta { get; set; } = 10;

    // cppcache SystemProperties.cpp:115 — DefaultMaxSocketBufferSize = 65 * 1024
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    // cppcache SystemProperties.cpp:116 — DefaultPingInterval = 10s
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    // cppcache SystemProperties.cpp:117 — DefaultRedundancyMonitorInterval = 10s
    public TimeSpan RedundancyMonitorInterval { get; set; } = TimeSpan.FromSeconds(10);

    // cppcache SystemProperties.cpp:118 — DefaultNotifyAckInterval = 1s
    public TimeSpan NotifyAckInterval { get; set; } = TimeSpan.FromSeconds(1);

    // cppcache SystemProperties.cpp:119 — DefaultNotifyDupCheckLife = 300s
    public TimeSpan NotifyDupCheckLife { get; set; } = TimeSpan.FromSeconds(300);

    // cppcache SystemProperties.hpp:386 — m_securityPropertiesPtr (shared_ptr<Properties> bag)
    public IReadOnlyDictionary<string, string> SecurityProperties { get; set; } =
        new Dictionary<string, string>();

    // cppcache SystemProperties.hpp:294 — securityClientDhAlgo (marked _GEODE_DEPRECATED_)
    public string SecurityClientDhAlgo { get; set; } = string.Empty;

    // cppcache SystemProperties.hpp:299 — securityClientKsPath
    public string SecurityClientKsPath { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:80 — DefaultDurableClientId = ""
    public string DurableClientId { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:81 — DefaultDurableTimeout = 300s
    public TimeSpan DurableTimeout { get; set; } = TimeSpan.FromSeconds(300);

    // cppcache SystemProperties.cpp:83 — DefaultConnectTimeout = 59s
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(59);

    // cppcache SystemProperties.cpp:84 — DefaultConnectWaitTimeout = 0 (Linux only)
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.Zero;

    // cppcache SystemProperties.cpp:85 — DefaultBucketWaitTimeout = 0 (Linux only)
    public TimeSpan BucketWaitTimeout { get; set; } = TimeSpan.Zero;

    // cppcache SystemProperties.cpp:98 — DefaultAutoReadyForEvents = true
    public bool AutoReadyForEvents { get; set; } = true;

    // cppcache SystemProperties.cpp:99 — DefaultSslEnabled = false
    public bool SslEnabled { get; set; }

    // cppcache SystemProperties.cpp:100 — DefaultTimeStatisticsEnabled = false
    public bool TimeStatisticsEnabled { get; set; }

    // cppcache SystemProperties.cpp:102 — DefaultSslKeyStore = ""
    public string SslKeyStore { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:103 — DefaultSslTrustStore = ""
    public string SslTrustStore { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:104 — DefaultSslKeystorePassword = ""
    public string SslKeystorePassword { get; set; } = string.Empty;

    // cppcache SystemProperties.cpp:78 — DefaultConflateEvents = "server"
    public string ConflateEvents { get; set; } = "server";

    // cppcache SystemProperties.cpp:121 — DefaultThreadPoolSize = hardware_concurrency * 2
    public uint ThreadPoolSize { get; set; } = (uint)(Environment.ProcessorCount * 2);

    // cppcache SystemProperties.cpp:122 — DefaultSuspendedTxTimeout = 30s
    public TimeSpan SuspendedTxTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // cppcache SystemProperties.cpp:123 — DefaultTombstoneTimeout = 480s
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromSeconds(480);

    // cppcache SystemProperties.cpp:125 — DefaultEnableChunkHandlerThread = false
    public bool EnableChunkHandlerThread { get; set; }

    // cppcache SystemProperties.cpp:126 — DefaultOnClientDisconnectClearPdxTypeIds = false
    public bool OnClientDisconnectClearPdxTypeIds { get; set; }

    // ── Serialization safety caps ────────────────────────────────────
    //
    // No cppcache analog — our additions to defend against hostile / buggy
    // wire payloads claiming gigabyte-scale lengths. Tuning is the host's
    // call: raise for legitimate bulk-data workloads, lower for tighter
    // sandboxing. Validated at host build time by the Hosting package's
    // options validator.

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
