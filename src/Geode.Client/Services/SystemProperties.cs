using Geode.Client.Options;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// Per-cache settings bag — built once at cache initialization from the
/// caller's <see cref="GeodeClientOptions"/> snapshot. Mirror of cppcache
/// <c>SystemProperties</c> (<c>cppcache/include/geode/SystemProperties.hpp</c>);
/// defaults match the <c>Default*</c> constants in
/// <c>cppcache/src/SystemProperties.cpp:78-126</c>.
/// </summary>
/// <remarks>
/// Properties expose <c>private set</c> so <see cref="MergeSystemProperties"/>
/// can populate them in place after default-construction; from the outside
/// the bag is read-only. cppcache exposes runtime setters on a handful of
/// fields (<c>setLogLevel</c> / <c>setEnableChunkHandlerThread</c> / ...);
/// not mirrored until a real consumer needs them.
/// </remarks>
internal sealed class SystemProperties
{
    // cppcache SystemProperties.cpp:87 — DefaultSamplingInterval = 1s
    public TimeSpan StatisticsSampleInterval { get; private set; } = TimeSpan.FromSeconds(1);

    // cppcache SystemProperties.cpp:88 — DefaultSamplingEnabled = false
    public bool StatisticsEnabled { get; private set; }

    // cppcache SystemProperties.cpp:90 — DefaultStatArchive = "statArchive.gfs"
    public string StatisticsArchiveFile { get; private set; } = "statArchive.gfs";

    // cppcache SystemProperties.cpp:91 — DefaultLogFilename = "" (stdout)
    public string LogFilename { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:93-94 — DefaultLogLevel = LogLevel::Config.
    // .NET LogLevel has no "Config"; Information is the closest level.
    public LogLevel LogLevel { get; private set; } = LogLevel.Information;

    // cppcache SystemProperties.cpp:144 — m_disableShufflingEndpoint(false)
    public bool DisableShufflingEndpoint { get; private set; }

    // cppcache SystemProperties.hpp:233 — DefaultName = ""
    public string Name { get; private set; } = string.Empty;

    // cppcache SystemProperties.hpp:235 — DefaultCacheXMLFile = ""
    public string CacheXMLFile { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:107 — DefaultLogFileSizeLimit = 0 (unlimited)
    public uint LogFileSizeLimit { get; private set; }

    // cppcache SystemProperties.cpp:108 — DefaultLogDiskSpaceLimit = 0 (unlimited)
    public uint LogDiskSpaceLimit { get; private set; }

    // cppcache SystemProperties.cpp:109 — DefaultStatsFileSizeLimit = 0 (unlimited)
    public uint StatsFileSizeLimit { get; private set; }

    // cppcache SystemProperties.cpp:110 — DefaultStatsDiskSpaceLimit = 0 (unlimited)
    public uint StatsDiskSpaceLimit { get; private set; }

    // cppcache SystemProperties.cpp:96 — DefaultConnectionPoolSize = 5
    public uint ConnectionPoolSize { get; private set; } = 5;

    // cppcache SystemProperties.cpp:112 — DefaultHeapLRULimit = 0 (disabled)
    public long HeapLRULimit { get; private set; }

    // cppcache SystemProperties.cpp:113 — DefaultHeapLRUDelta = 10 (% eviction step)
    public int HeapLRUDelta { get; private set; } = 10;

    // cppcache SystemProperties.cpp:115 — DefaultMaxSocketBufferSize = 65 * 1024
    public int MaxSocketBufferSize { get; private set; } = 65 * 1024;

    // cppcache SystemProperties.cpp:116 — DefaultPingInterval = 10s
    public TimeSpan PingInterval { get; private set; } = TimeSpan.FromSeconds(10);

    // cppcache SystemProperties.cpp:117 — DefaultRedundancyMonitorInterval = 10s
    public TimeSpan RedundancyMonitorInterval { get; private set; } = TimeSpan.FromSeconds(10);

    // cppcache SystemProperties.cpp:118 — DefaultNotifyAckInterval = 1s
    public TimeSpan NotifyAckInterval { get; private set; } = TimeSpan.FromSeconds(1);

    // cppcache SystemProperties.cpp:119 — DefaultNotifyDupCheckLife = 300s
    public TimeSpan NotifyDupCheckLife { get; private set; } = TimeSpan.FromSeconds(300);

    // cppcache SystemProperties.hpp:386 — m_securityPropertiesPtr (shared_ptr<Properties> bag)
    public IReadOnlyDictionary<string, string> SecurityProperties { get; private set; } =
        new Dictionary<string, string>();

    // cppcache SystemProperties.hpp:294 — securityClientDhAlgo (marked _GEODE_DEPRECATED_)
    public string SecurityClientDhAlgo { get; private set; } = string.Empty;

    // cppcache SystemProperties.hpp:299 — securityClientKsPath
    public string SecurityClientKsPath { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:80 — DefaultDurableClientId = ""
    public string DurableClientId { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:81 — DefaultDurableTimeout = 300s
    public TimeSpan DurableTimeout { get; private set; } = TimeSpan.FromSeconds(300);

    // cppcache SystemProperties.cpp:83 — DefaultConnectTimeout = 59s
    public TimeSpan ConnectTimeout { get; private set; } = TimeSpan.FromSeconds(59);

    // cppcache SystemProperties.cpp:84 — DefaultConnectWaitTimeout = 0 (Linux only)
    public TimeSpan ConnectWaitTimeout { get; private set; } = TimeSpan.Zero;

    // cppcache SystemProperties.cpp:85 — DefaultBucketWaitTimeout = 0 (Linux only)
    public TimeSpan BucketWaitTimeout { get; private set; } = TimeSpan.Zero;

    // cppcache SystemProperties.cpp:98 — DefaultAutoReadyForEvents = true
    public bool AutoReadyForEvents { get; private set; } = true;

    // cppcache SystemProperties.cpp:99 — DefaultSslEnabled = false
    public bool SslEnabled { get; private set; }

    // cppcache SystemProperties.cpp:100 — DefaultTimeStatisticsEnabled = false
    public bool TimeStatisticsEnabled { get; private set; }

    // cppcache SystemProperties.cpp:102 — DefaultSslKeyStore = ""
    public string SslKeyStore { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:103 — DefaultSslTrustStore = ""
    public string SslTrustStore { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:104 — DefaultSslKeystorePassword = ""
    public string SslKeystorePassword { get; private set; } = string.Empty;

    // cppcache SystemProperties.cpp:78 — DefaultConflateEvents = "server"
    public string ConflateEvents { get; private set; } = "server";

    // cppcache SystemProperties.cpp:121 — DefaultThreadPoolSize = hardware_concurrency * 2
    public uint ThreadPoolSize { get; private set; } = (uint)(Environment.ProcessorCount * 2);

    // cppcache SystemProperties.cpp:122 — DefaultSuspendedTxTimeout = 30s
    public TimeSpan SuspendedTxTimeout { get; private set; } = TimeSpan.FromSeconds(30);

    // cppcache SystemProperties.cpp:123 — DefaultTombstoneTimeout = 480s
    public TimeSpan TombstoneTimeout { get; private set; } = TimeSpan.FromSeconds(480);

    // cppcache SystemProperties.cpp:125 — DefaultEnableChunkHandlerThread = false
    public bool EnableChunkHandlerThread { get; private set; }

    // cppcache SystemProperties.cpp:126 — DefaultOnClientDisconnectClearPdxTypeIds = false
    public bool OnClientDisconnectClearPdxTypeIds { get; private set; }

    // ── Serialization safety caps ────────────────────────────────────
    //
    // The four below stay { get; set; } (publicly mutable) rather than
    // { get; private set; }. They have no cppcache analog — they're our
    // own additions to defend against hostile / buggy wire payloads
    // claiming gigabyte-scale lengths. Tuning is the host's call (raise
    // for legitimate bulk-data workloads, lower for tighter sandboxing),
    // hence the open setter. Validation is at host build time in
    // GeodeClientOptionsValidator.

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

    /// <summary>
    /// Populate <paramref name="properties"/> in place from <paramref name="opts"/>
    /// (or leave defaults if null) and stamp <paramref name="name"/> last so it
    /// wins over <c>opts.Name</c>. Called from
    /// <see cref="GeodeCache.InitializeCoreAsync"/> on a freshly-default-constructed
    /// instance — caller must not share the instance until this returns.
    /// </summary>
    internal static void MergeSystemProperties(SystemProperties properties,
        string? name = null, GeodeClientOptions? opts = null)
    {
        if (opts is not null)
        {
            properties.Name = opts.Name;
            properties.ThreadPoolSize = opts.ThreadPoolSize;

            // Subscription
            properties.DurableClientId = opts.Subscription.DurableClientId;
            properties.DurableTimeout = opts.Subscription.DurableTimeout;
            properties.AutoReadyForEvents = opts.Subscription.AutoReadyForEvents;
            properties.RedundancyMonitorInterval = opts.Subscription.RedundancyMonitorInterval;
            properties.NotifyAckInterval = opts.Subscription.NotifyAckInterval;
            properties.NotifyDupCheckLife = opts.Subscription.NotifyDupCheckLife;

            // Security
            properties.SecurityClientDhAlgo = opts.Security.ClientDhAlgo;
            properties.SecurityClientKsPath = opts.Security.ClientKsPath;
            properties.SecurityProperties = opts.Security.Properties;

            // Heap (LRULimit: ulong public ↔ long internal — wire is i64 BE,
            // cast is safe within the positive-i64 range we care about).
            properties.HeapLRULimit = (long)opts.Heap.LRULimit;
            properties.HeapLRUDelta = opts.Heap.LRUDelta;

            // Tls — only Enabled has a SystemProperties analog today;
            // KeyStorePath / Password / TrustStorePath wire in when the
            // SSL handshake path lands (Phase 3+).
            properties.SslEnabled = opts.Tls.Enabled;

            // Pool — surfaced under SystemProperties for cppcache parity;
            // PoolAttributes owns the per-pool overrides.
            properties.ConnectionPoolSize = (uint)opts.Pool.ConnectionPoolSize;
            properties.ConnectTimeout = opts.Pool.ConnectTimeout;
            properties.ConnectWaitTimeout = opts.Pool.ConnectWaitTimeout;
            properties.MaxSocketBufferSize = opts.Pool.MaxSocketBufferSize;
            properties.PingInterval = opts.Pool.PingInterval;
            properties.BucketWaitTimeout = opts.Pool.BucketWaitTimeout;
            properties.DisableShufflingEndpoint = !opts.Pool.ShuffleEndpoints;   // inverted (cppcache parity)

            // Serialization safety caps
            properties.MaxDepth = opts.Serialization.MaxDepth;
            properties.MaxArrayLength = opts.Serialization.MaxArrayLength;
            properties.MaxBytesLength = opts.Serialization.MaxBytesLength;
            properties.MaxStringLength = opts.Serialization.MaxStringLength;

            // TODO Phase 2+ — opts fields without a SystemProperties analog yet:
            //   opts.EnableChunkHandlerThread     (.NET ThreadPool covers; may stay unmapped)
            //   opts.Tls.KeyStorePath/Password/TrustStorePath (SSL handshake)
            //   opts.Subscription.ConflateEvents  (subscription queue settings)
            //   opts.Heap.TombstoneTimeout        (concurrency-checks / tombstones)
            //   opts.Pdx.ClearTypeIdsOnDisconnect (PDX type registry)
            //   opts.Tx.SuspendedTimeout          (transactions, Phase 11+)
        }

        // name parameter wins over opts.Name when both supplied — caller
        // (factory) names the cache; opts is just the seed.
        if (!string.IsNullOrEmpty(name))
        {
            properties.Name = name;
        }
    }
}
