using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Per-cache client-wide settings bag. Mirrors cppcache
/// <c>SystemProperties</c> (<c>cppcache/include/geode/SystemProperties.hpp</c>)
/// 1:1, all fields included; built from the per-cache
/// <c>GeodeClientOptions</c> snapshot at scope-construction time.
/// </summary>
/// <remarks>
/// Defaults match cppcache <c>Default*</c> constants in
/// <c>cppcache/src/SystemProperties.cpp:78-126</c>. Fields are <c>init</c>-only:
/// settings are bound once when the scope is built and read-only thereafter.
/// cppcache exposes runtime setters on a handful of fields
/// (<c>setLogLevel</c> / <c>setEnableChunkHandlerThread</c> / etc.); not
/// mirrored until a real consumer needs them.
/// </remarks>
internal sealed class SystemProperties
{
    // SystemProperties.cpp:87 — DefaultSamplingInterval = seconds(1)
    public TimeSpan StatisticsSampleInterval { get; init; } = TimeSpan.FromSeconds(1);

    // SystemProperties.cpp:88 — DefaultSamplingEnabled = false
    public bool StatisticsEnabled { get; init; }

    // SystemProperties.cpp:90 — DefaultStatArchive = "statArchive.gfs"
    public string StatisticsArchiveFile { get; init; } = "statArchive.gfs";

    // SystemProperties.cpp:91 — DefaultLogFilename = "" (stdout)
    public string LogFilename { get; init; } = string.Empty;

    // SystemProperties.cpp:93-94 — DefaultLogLevel = LogLevel::Config.
    // .NET LogLevel has no "Config"; Information is the closest level.
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    // SystemProperties.cpp:144 — m_disableShufflingEndpoint(false)
    public bool DisableShufflingEndpoint { get; init; }

    // SystemProperties.hpp:233 — name() / DefaultName = ""
    public string Name { get; init; } = string.Empty;

    // SystemProperties.hpp:235 — cacheXMLFile() / DefaultCacheXMLFile = ""
    public string CacheXMLFile { get; init; } = string.Empty;

    // SystemProperties.cpp:107 — DefaultLogFileSizeLimit = 0 (unlimited)
    public uint LogFileSizeLimit { get; init; }

    // SystemProperties.cpp:108 — DefaultLogDiskSpaceLimit = 0 (unlimited)
    public uint LogDiskSpaceLimit { get; init; }

    // SystemProperties.cpp:109 — DefaultStatsFileSizeLimit = 0 (unlimited)
    public uint StatsFileSizeLimit { get; init; }

    // SystemProperties.cpp:110 — DefaultStatsDiskSpaceLimit = 0 (unlimited)
    public uint StatsDiskSpaceLimit { get; init; }

    // SystemProperties.cpp:96 — DefaultConnectionPoolSize = 5
    public uint ConnectionPoolSize { get; init; } = 5;

    // SystemProperties.cpp:112 — DefaultHeapLRULimit = 0 (disabled)
    public long HeapLRULimit { get; init; }

    // SystemProperties.cpp:113 — DefaultHeapLRUDelta = 10 (% eviction step)
    public int HeapLRUDelta { get; init; } = 10;

    // SystemProperties.cpp:115 — DefaultMaxSocketBufferSize = 65 * 1024
    public int MaxSocketBufferSize { get; init; } = 65 * 1024;

    // SystemProperties.cpp:116 — DefaultPingInterval = seconds(10)
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(10);

    // SystemProperties.cpp:117 — DefaultRedundancyMonitorInterval = seconds(10)
    public TimeSpan RedundancyMonitorInterval { get; init; } = TimeSpan.FromSeconds(10);

    // SystemProperties.cpp:118 — DefaultNotifyAckInterval = seconds(1)
    public TimeSpan NotifyAckInterval { get; init; } = TimeSpan.FromSeconds(1);

    // SystemProperties.cpp:119 — DefaultNotifyDupCheckLife = seconds(300)
    public TimeSpan NotifyDupCheckLife { get; init; } = TimeSpan.FromSeconds(300);

    // SystemProperties.hpp:386 — m_securityPropertiesPtr (shared_ptr<Properties> bag)
    public IReadOnlyDictionary<string, string> SecurityProperties { get; init; } =
        new Dictionary<string, string>();

    // SystemProperties.hpp:294 — securityClientDhAlgo; marked _GEODE_DEPRECATED_
    [Obsolete("Diffie-Hellman based credentials encryption is not supported.")]
    public string SecurityClientDhAlgo { get; init; } = string.Empty;

    // SystemProperties.hpp:299 — securityClientKsPath
    public string SecurityClientKsPath { get; init; } = string.Empty;

    // SystemProperties.cpp:80 — DefaultDurableClientId = ""
    public string DurableClientId { get; init; } = string.Empty;

    // SystemProperties.cpp:81 — DefaultDurableTimeout = seconds(300)
    public TimeSpan DurableTimeout { get; init; } = TimeSpan.FromSeconds(300);

    // SystemProperties.cpp:83 — DefaultConnectTimeout = seconds(59)
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(59);

    // SystemProperties.cpp:84 — DefaultConnectWaitTimeout = zero (Linux only)
    public TimeSpan ConnectWaitTimeout { get; init; } = TimeSpan.Zero;

    // SystemProperties.cpp:85 — DefaultBucketWaitTimeout = zero (Linux only)
    public TimeSpan BucketWaitTimeout { get; init; } = TimeSpan.Zero;

    // SystemProperties.cpp:98 — DefaultAutoReadyForEvents = true
    public bool AutoReadyForEvents { get; init; } = true;

    // SystemProperties.cpp:99 — DefaultSslEnabled = false
    public bool SslEnabled { get; init; }

    // SystemProperties.cpp:100 — DefaultTimeStatisticsEnabled = false
    public bool TimeStatisticsEnabled { get; init; }

    // SystemProperties.cpp:102 — DefaultSslKeyStore = ""
    public string SslKeyStore { get; init; } = string.Empty;

    // SystemProperties.cpp:103 — DefaultSslTrustStore = ""
    public string SslTrustStore { get; init; } = string.Empty;

    // SystemProperties.cpp:104 — DefaultSslKeystorePassword = ""
    public string SslKeystorePassword { get; init; } = string.Empty;

    // SystemProperties.cpp:78 — DefaultConflateEvents = "server"
    public string ConflateEvents { get; init; } = "server";

    // SystemProperties.cpp:121 — DefaultThreadPoolSize = hardware_concurrency * 2
    public uint ThreadPoolSize { get; init; } = (uint)(Environment.ProcessorCount * 2);

    // SystemProperties.cpp:122 — DefaultSuspendedTxTimeout = seconds(30)
    public TimeSpan SuspendedTxTimeout { get; init; } = TimeSpan.FromSeconds(30);

    // SystemProperties.cpp:123 — DefaultTombstoneTimeout = seconds(480)
    public TimeSpan TombstoneTimeout { get; init; } = TimeSpan.FromSeconds(480);

    // SystemProperties.cpp:125 — DefaultEnableChunkHandlerThread = false
    public bool EnableChunkHandlerThread { get; init; }

    // SystemProperties.cpp:126 — DefaultOnClientDisconnectClearPdxTypeIds = false
    public bool OnClientDisconnectClearPdxTypeIds { get; init; }
}
