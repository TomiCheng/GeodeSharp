namespace Geode.Client.Options;

/// <summary>Connection-pool tuning mirroring cppcache <c>SystemProperties</c>; mirror-then-prune per CLAUDE.md.</summary>
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

    /// <summary>TCP connections to maintain per endpoint; <c>connection-pool-size</c>; default 5; <c>0</c> = unlimited.</summary>
    /// <remarks>Per-endpoint. cppcache: <c>SystemProperties.cpp:318</c>, <c>TcrEndpoint.cpp:49</c> (one slot reserved for subscription channel).</remarks>
    public int ConnectionPoolSize { get; set; } = 5;

    /// <summary>
    /// Budget for opening a new server connection (TCP connect + Geode
    /// handshake combined).
    /// </summary>
    /// <remarks>
    /// default 59s.
    /// </remarks>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(59);

    /// <summary>Extra wait between failed connects; <c>connect-wait-timeout</c>; default zero (disabled).</summary>
    /// <remarks>Per-connection. Linux-only in cppcache (<c>#ifdef __linux</c>, <c>TcrEndpoint.cpp:112-133</c>) — workaround for EPIPE on connect. .NET async sockets don't exhibit it; prune candidate.</remarks>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Socket send/receive buffer size; <c>max-socket-buffer-size</c>; default 65 KiB.</summary>
    /// <remarks>Per-connection. cppcache: <c>SystemProperties.cpp:275</c>, applied via <c>SO_SNDBUF</c>/<c>SO_RCVBUF</c> at <c>TcpConn.cpp:123</c>.</remarks>
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    /// <summary>
    /// Idle keep-alive ping cadence; <c>ping-interval</c>; default 10s.
    /// </summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether to randomise server-list order at pool construction; cppcache <c>disable-shuffling-of-endpoints</c> inverted; default <c>true</c>.</summary>
    /// <remarks>Pool-level. cppcache: <c>SystemProperties.cpp:297</c>, <c>ThinClientPoolDM.cpp:199-203</c>. Load-balances across clients, not a runtime reorder.</remarks>
    public bool ShuffleEndpoints { get; set; } = true;

    /// <summary>How long a partitioned-region op waits for primary-bucket availability; <c>bucket-wait-timeout</c>; default zero.</summary>
    /// <remarks>Pool-level. cppcache: <c>SystemProperties.cpp:295</c>, <c>ClientMetadataService.cpp:45/133/734/790</c>. Single-hop routing is Phase 4+; mirror only.</remarks>
    public TimeSpan BucketWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Deep clone via copy constructor.</summary>
    public PoolOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        // Defensive — negative TimeSpan for a connect budget makes no sense
        // (cppcache parseDurationProperty silently accepts it; we don't).
        if (ConnectTimeout < TimeSpan.Zero)
            yield return $"{prefix}.ConnectTimeout must be >= 0 (got {ConnectTimeout}).";
    }
}
