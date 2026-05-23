/*
namespace Geode.Client.Options;

/// <summary>
/// Connection pool.
/// </summary>
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
    /// Cap on TCP connections per endpoint; the pool stops opening new
    /// connections to that endpoint once this many are in use. Sits
    /// beneath the pool-wide
    /// <see cref="CachePoolOptions.MaxConnections"/>.
    /// </summary>
    /// <remarks>
    /// default 5; <c>0</c> = unlimited (no per-endpoint cap); must be <c>&gt;= 0</c>.
    /// </remarks>
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

    /// <summary>
    /// Socket send/receive buffer size; default 65 KiB.
    /// </summary>
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    /// <summary>
    /// Idle keep-alive ping cadence; default 10s.
    /// </summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to randomise server-list order at pool construction; default <c>true</c>.
    /// </summary>
    public bool ShuffleEndpoints { get; set; } = true;

    /// <summary>
    /// How long a partitioned-region op waits for primary-bucket availability; default zero.
    /// </summary>
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

        if (ConnectionPoolSize < 0)
            yield return $"{prefix}.ConnectionPoolSize must be >= 0 (got {ConnectionPoolSize}).";
    }
}

*/