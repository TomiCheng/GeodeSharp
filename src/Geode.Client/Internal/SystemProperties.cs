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

    /// <summary>
    /// Maximum nested-container depth allowed when serialising or
    /// deserialising wire payloads. Default <b>64</b> (matches
    /// <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>).
    /// Must be <c>&gt;= 1</c>; validated at host build time by
    /// <c>GeodeClientOptionsValidator</c>.
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// Maximum element count for any single length-prefixed wire
    /// payload — <see cref="byte"/><c>[]</c>, primitive arrays
    /// (<see cref="int"/><c>[]</c> / <see cref="double"/><c>[]</c> /
    /// …), <c>CacheableObjectArray</c> / <c>CacheableStringArray</c>,
    /// and the Tier B-2 collection types
    /// (<c>List&lt;T&gt;</c> / <c>HashSet&lt;T&gt;</c> /
    /// <c>Dictionary&lt;K,V&gt;</c> / <c>LinkedList&lt;T&gt;</c> /
    /// <c>Stack&lt;T&gt;</c>). Default <b>1,000,000</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threat model.</b> A hostile or buggy server can claim any
    /// <c>int32</c> length in the wire-length prefix. Without a cap,
    /// the client reads "I will give you 2,000,000,000 elements" and
    /// allocates 8&#xA0;GB up front — instant OOM. Capping the
    /// length on read forces a fast, clear failure
    /// (<c>GeodeException</c>) instead.
    /// </para>
    /// <para>
    /// <b>Inclusive bound.</b> The check is <c>length &gt; MaxArrayLength</c>
    /// — a payload claiming exactly <see cref="MaxArrayLength"/>
    /// elements is accepted; one element more is rejected. <c>0</c>
    /// is the unit-of-measure (allow only empty arrays);
    /// <see cref="GeodeClientOptionsValidator"/> rejects negative
    /// values.
    /// </para>
    /// <para>
    /// <b>Default rationale.</b> Geode best practice is to keep a
    /// single cached value under ~1&#xA0;MB. A million elements
    /// covers <c>int[]</c> up to 4&#xA0;MB and <c>byte[]</c> up to
    /// 1&#xA0;MB — comfortably above realistic workloads while
    /// rejecting hostile gigabyte-scale claims. Tune up for legitimate
    /// bulk-data use cases.
    /// </para>
    /// <para>
    /// <b>Write side</b> applies the same cap (caller bug guard) —
    /// crossing the limit on encode throws
    /// <see cref="InvalidOperationException"/>; on decode it throws
    /// <c>GeodeException</c>. Symmetric so we never emit a payload
    /// our own reader would refuse.
    /// </para>
    /// </remarks>
    public int MaxArrayLength { get; set; } = 1_000_000;

    /// <summary>
    /// Maximum byte count for a single <see cref="byte"/><c>[]</c>
    /// payload (<see cref="DSCode.CacheableBytes"/> = 46). Default
    /// <b>10,000,000</b> (10&#xA0;MB).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split out from <see cref="MaxArrayLength"/> because
    /// <see cref="byte"/><c>[]</c> serves a different role on Geode:
    /// it's the canonical "binary blob" type — file content,
    /// serialised objects from another stack, encrypted payloads —
    /// which has a wholly different natural size distribution from
    /// "a primitive array with many small elements". Typical blob
    /// caches range from hundreds of KB to a few MB; the
    /// <see cref="MaxArrayLength"/> default of 1&#xA0;M would
    /// gratuitously reject those. Tune up to 100&#xA0;MB or down to
    /// 1&#xA0;MB depending on the workload.
    /// </para>
    /// <para>
    /// <b>Same threat-model and check semantics</b> as
    /// <see cref="MaxArrayLength"/>: hostile servers can claim any
    /// <c>int32</c> length in the VL-encoded prefix; on read we
    /// refuse before allocating. Inclusive bound (<c>length &gt; MaxBytesLength</c>);
    /// <c>0</c> legal; negative rejected at host build time.
    /// </para>
    /// </remarks>
    public int MaxBytesLength { get; set; } = 10_000_000;

    /// <summary>
    /// Maximum length for any single string payload — covers all four
    /// <c>CacheableString</c> wire variants (DSCodes 42 / 87 / 88 /
    /// 89). Unit is whatever the wire format puts in the length
    /// prefix for that variant (modified-UTF-8 byte count for 42,
    /// char count for the other three). Default <b>1,000,000</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threat model</b> matches <see cref="MaxArrayLength"/>: the
    /// "huge" string variants (88 / 89) carry an i32 length prefix
    /// which a hostile server can pin at 2&#xA0;billion, forcing a
    /// multi-gigabyte allocation. Cap on read prevents that.
    /// </para>
    /// <para>
    /// <b>Why a separate limit from <see cref="MaxArrayLength"/>.</b>
    /// Strings and bulk-data arrays have different "natural" size
    /// distributions — a 1&#xA0;MB JSON-shaped string is not unusual,
    /// while a 1&#xA0;MB-element collection is. Keeping the two
    /// configurable independently lets users tune one without
    /// loosening the other. Same default for now (1 M) so a stock
    /// install behaves consistently.
    /// </para>
    /// <para>
    /// <b>Inclusive bound and validation</b> follow the same rules as
    /// <see cref="MaxArrayLength"/>: <c>length &gt; MaxStringLength</c>
    /// fails; <c>0</c> is legal (empty strings only); negative
    /// rejected at host build time.
    /// </para>
    /// </remarks>
    public int MaxStringLength { get; set; } = 1_000_000;
}
