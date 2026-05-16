namespace Geode.Client.Options;

/// <summary>
/// Connection-pool tuning derived from cppcache
/// <c>SystemProperties</c>. Defaults match cppcache's own constants in
/// <c>SystemProperties.cpp</c> so behaviour is interchangeable until we
/// have reason to diverge.
/// </summary>
/// <remarks>
/// Per CLAUDE.md "mirror then prune": every cppcache pool key is
/// mirrored verbatim while we port; pruning to a .NET-native schema
/// happens once at the end of Phase 1.5 / before the first NuGet
/// release. Each property's remarks record where cppcache parses /
/// consumes it (file:line), the abstraction level (pool / endpoint /
/// connection), and any platform-specific quirks.
/// </remarks>
public class PoolOptions : ICloneable
{
    public PoolOptions() { }

    public PoolOptions(PoolOptions other)
    {
        ConnectionPoolSize = other.ConnectionPoolSize;
        ConnectTimeout = other.ConnectTimeout;
        ConnectWaitTimeout = other.ConnectWaitTimeout;
        MaxSocketBufferSize = other.MaxSocketBufferSize;
        PingInterval = other.PingInterval;
        ShuffleEndpoints = other.ShuffleEndpoints;
        BucketWaitTimeout = other.BucketWaitTimeout;
    }

    /// <summary>
    /// Number of TCP connections to maintain &#x2014; cppcache
    /// <c>connection-pool-size</c>; default 5.
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> <b>per-endpoint (per server), not
    /// pool-wide.</b> Each <c>TcrEndpoint</c> tracks its own
    /// <c>m_maxConnections</c>.</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:318-319</c> &#x2014;
    /// <c>m_connectionPoolSize</c>. Default constant
    /// <c>DefaultConnectionPoolSize = 5</c> at line 96.</para>
    /// <para><b>Consumed:</b>
    /// <c>cppcache/src/TcrEndpoint.cpp:49-51</c>
    /// (<c>m_maxConnections = sysProp.connectionPoolSize()</c>). The
    /// endpoint pre-creates <c>maxConnections - 1</c> operation
    /// connections &#x2014; one slot is reserved for the subscription
    /// channel &#x2014; and queues them in <c>m_opConnections</c>
    /// (lines 390-419).</para>
    /// <para><b>Special value:</b> <c>0</c> = unlimited.</para>
    /// <para><b>.NET mapping:</b> no built-in equivalent;
    /// <c>SocketsHttpHandler</c> has no per-host cap. Custom pool
    /// logic. Naming and pool-wide vs per-endpoint semantics are the
    /// main design decision deferred to Phase 1.5.</para>
    /// </remarks>
    public int ConnectionPoolSize { get; set; } = 5;

    /// <summary>
    /// Time budget for the TCP connect + handshake &#x2014; cppcache
    /// <c>connect-timeout</c>; default 59 seconds.
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> per-connection.</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:291-292</c>. Default
    /// <c>DefaultConnectTimeout = std::chrono::seconds(59)</c> at line
    /// 83.</para>
    /// <para><b>Consumed:</b>
    /// <list type="bullet">
    ///   <item><c>cppcache/src/TcrEndpoint.cpp:343-346, 410-413</c>
    ///   &#x2014; server handshake / op connection.</item>
    ///   <item><c>cppcache/src/TcrEndpoint.cpp:445-448</c> &#x2014;
    ///   notification channel uses <c>connectTimeout() * 3</c>.</item>
    ///   <item><c>cppcache/src/TcrPoolEndPoint.cpp:71, 87</c> &#x2014;
    ///   pool endpoint; subscription channel also &#xD7;3.</item>
    ///   <item><c>cppcache/src/ThinClientLocatorHelper.cpp:95</c>
    ///   &#x2014; locator handshake.</item>
    /// </list>
    /// Passed straight into the <c>TcpConn</c> / <c>TcpSslConn</c>
    /// constructor as the boost::asio connect timeout
    /// (<c>cppcache/src/TcrConnection.cpp:131-137</c>).</para>
    /// <para><b>Subscription &#xD7;3 multiplier</b> is an internal
    /// magic number in cppcache; replicate when Phase 2 / 3
    /// subscription is implemented.</para>
    /// <para><b>.NET mapping:</b> <c>Socket.ConnectAsync</c> with a
    /// linked <c>CancellationTokenSource</c> on this duration.</para>
    /// </remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(59);

    /// <summary>
    /// Extra wait between failed connect attempts &#x2014; cppcache
    /// <c>connect-wait-timeout</c>; default <see cref="TimeSpan.Zero"/>
    /// (= disabled).
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> per-connection.</para>
    /// <para><b>Platform:</b> <b>Linux only.</b> cppcache gates the
    /// entire feature with <c>#ifdef __linux</c> at
    /// <c>cppcache/src/TcrEndpoint.cpp:112-133</c>; on Windows /
    /// macOS the function returns <c>false</c> at line 121 without
    /// reading this value.</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:293-294</c>. Default
    /// <c>std::chrono::seconds::zero()</c> at line 84.</para>
    /// <para><b>Consumed:</b>
    /// <c>TcrEndpoint::createNewConnectionWL()</c> &#x2014; a
    /// lock-based retry loop that re-acquires <c>m_connectLock</c>
    /// until <c>now + connectWaitTimeout</c>. Workaround for
    /// Linux-specific socket pipe / EPIPE errors during connection
    /// establishment.</para>
    /// <para><b>.NET mapping:</b> none; modern .NET async sockets
    /// don't exhibit the underlying issue. Strong pruning candidate
    /// at the end of Phase 1.5.</para>
    /// </remarks>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Send / receive buffer size hint for the underlying socket
    /// &#x2014; cppcache <c>max-socket-buffer-size</c>; default
    /// <c>65 * 1024</c> = 66560 bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> per-connection.</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:275-276</c>. Default
    /// <c>DefaultMaxSocketBufferSize = 65 * 1024</c> at line 115.</para>
    /// <para><b>Consumed:</b>
    /// <c>cppcache/src/TcrConnection.cpp:137</c> forwards the value
    /// to <c>TcpConn</c> / <c>TcpSslConn</c>, which apply it via
    /// boost::asio
    /// <c>socket_base::send_buffer_size</c> /
    /// <c>receive_buffer_size</c> at
    /// <c>cppcache/src/TcpConn.cpp:123-125</c>. Maps to the OS
    /// <c>SO_SNDBUF</c> / <c>SO_RCVBUF</c> options.</para>
    /// <para><b>.NET mapping:</b> <c>Socket.SendBufferSize</c> /
    /// <c>Socket.ReceiveBufferSize</c>.</para>
    /// </remarks>
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    /// <summary>
    /// Idle keep-alive ping cadence &#x2014; cppcache
    /// <c>ping-interval</c>; default 10 seconds.
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> endpoint-level (per connected server).</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:277-278</c>. Default
    /// <c>DefaultPingInterval = std::chrono::seconds(10)</c> at line
    /// 116.</para>
    /// <para><b>Consumed:</b>
    /// <c>cppcache/src/TcrConnectionManager.cpp:74-81</c> &#x2014; a
    /// <c>FunctionExpiryTask</c> is scheduled every
    /// <c>pingInterval</c> to call <c>ping_endpoints()</c> (lines
    /// 259-265), which sends <c>MessageType.Ping</c> to each
    /// connected endpoint.</para>
    /// <para><b>Caveat:</b> the schedule is gated by
    /// <c>if (!isPool)</c> at line 74. <b>Pool mode has its own
    /// keepalive path and this value may be inactive there.</b>
    /// Re-verify semantics during Phase 1.5 pool design.</para>
    /// <para><b>.NET mapping:</b> long-running background
    /// <c>PeriodicTimer</c> task per pool / endpoint.</para>
    /// </remarks>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to randomise the order in which servers are tried.
    /// cppcache uses the inverted
    /// <c>disable-shuffling-of-endpoints</c> (default <c>false</c>
    /// &#x21D2; shuffle by default), so the equivalent default here
    /// is <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> pool-level.</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:297-298</c>
    /// (<c>m_disableShufflingEndpoint</c>; default <c>false</c>).</para>
    /// <para><b>Consumed:</b>
    /// <c>cppcache/src/ThinClientPoolDM.cpp:199-203</c> &#x2014; when
    /// shuffling is enabled, <c>RandGen</c> picks a random starting
    /// index into <c>m_attrs-&gt;m_initServList</c>; iteration then
    /// proceeds in order from there. This is load balancing across
    /// clients, not a runtime reorder.</para>
    /// <para><b>.NET mapping:</b> custom &#x2014; randomise the
    /// server list once at pool construction.</para>
    /// </remarks>
    public bool ShuffleEndpoints { get; set; } = true;

    /// <summary>
    /// How long a partitioned-region operation waits for a primary
    /// bucket to become available before failing &#x2014; cppcache
    /// <c>bucket-wait-timeout</c>; default <see cref="TimeSpan.Zero"/>
    /// (= no extra wait).
    /// </summary>
    /// <remarks>
    /// <para><b>Level:</b> pool-level (partitioned-region routing
    /// metadata).</para>
    /// <para><b>Parsed:</b>
    /// <c>cppcache/src/SystemProperties.cpp:295-296</c>. Default
    /// <c>std::chrono::seconds::zero()</c> at line 85.</para>
    /// <para><b>Consumed:</b>
    /// <c>cppcache/src/ClientMetadataService.cpp:45-47</c> (init),
    /// <c>:133</c> (enables bucket-timeout tracking when &gt; 0),
    /// <c>:734</c> (early return if zero), <c>:790</c>
    /// (<c>isBucketMarkedForTimeout</c>). During single-hop routing,
    /// marks stale buckets to trigger metadata refresh.</para>
    /// <para><b>Status:</b> out of MVP scope (partitioned regions /
    /// single-hop are Phase 4+); included for parity during the
    /// cppcache audit window.</para>
    /// </remarks>
    public TimeSpan BucketWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Deep clone via copy constructor.</summary>
    public PoolOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
