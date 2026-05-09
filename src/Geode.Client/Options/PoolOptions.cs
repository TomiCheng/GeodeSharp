namespace Geode.Client.Options;

/// <summary>
/// Connection-pool tuning derived from cppcache
/// <c>SystemProperties</c>. Defaults match cppcache's own constants in
/// <c>SystemProperties.cpp</c> so behaviour is interchangeable until we
/// have reason to diverge.
/// </summary>
public class PoolOptions
{
    /// <summary>
    /// Number of TCP connections to maintain in the pool. Mirrors cppcache
    /// <c>connection-pool-size</c>; default 5.
    /// </summary>
    /// <remarks>
    /// Phase 6 (pool) consumer. CLAUDE.md schema splits this into
    /// <c>MinConnections</c> / <c>MaxConnections</c>; for now we expose a
    /// single fixed size like cppcache and revisit when the pool is built.
    /// </remarks>
    public int ConnectionPoolSize { get; set; } = 5;

    /// <summary>
    /// Time budget for the TCP connect + handshake. Mirrors cppcache
    /// <c>connect-timeout</c>; default 59 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(59);

    /// <summary>
    /// Extra wait between failed connect attempts. Mirrors cppcache
    /// <c>connect-wait-timeout</c>; default <see cref="TimeSpan.Zero"/>
    /// (= disabled). Linux-specific in cppcache; kept here for parity but
    /// likely unused by .NET socket APIs.
    /// </summary>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Send / receive buffer size hint for the underlying socket. Mirrors
    /// cppcache <c>max-socket-buffer-size</c>; default 65 × 1024 = 66560 bytes.
    /// </summary>
    public int MaxSocketBufferSize { get; set; } = 65 * 1024;

    /// <summary>
    /// Idle keep-alive ping cadence. Mirrors cppcache <c>ping-interval</c>;
    /// default 10 seconds. The pool sends a <c>MessageType.Ping</c> on idle
    /// connections at this rate so the server doesn't time them out.
    /// </summary>
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to randomise the order in which servers are tried.
    /// cppcache uses the inverted <c>disable-shuffling-of-endpoints</c>
    /// (default false ⇒ shuffle by default), so the equivalent default here
    /// is <c>true</c>.
    /// </summary>
    public bool ShuffleEndpoints { get; set; } = true;
}
