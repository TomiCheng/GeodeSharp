using Microsoft.Extensions.Options;

namespace Geode.Client;

/// <summary>
/// Fluent builder for connection pools; obtain via <see cref="IPoolManager.CreateFactory"/>.
/// Mirrors cppcache <c>PoolFactory</c>
/// (<c>cppcache/include/geode/PoolFactory.hpp</c>).
/// </summary>
/// <remarks>
/// Setters mutate the internal attributes; <see cref="BuildAsync"/>
/// snapshots them so further factory mutations don't affect already-built
/// pools.
/// </remarks>
public interface IPoolFactory
{
    /// <summary>Reset all attributes back to defaults.</summary>
    IPoolFactory Reset();

    /// <summary>How long an op may wait for a free connection when the pool has hit its max.</summary>
    IPoolFactory SetFreeConnectionTimeout(TimeSpan connectionTimeout);

    /// <summary>How often connections are rotated to rebalance load across the server cluster.</summary>
    IPoolFactory SetLoadConditioningInterval(TimeSpan loadConditioningInterval);

    /// <summary>TCP send/receive buffer size in bytes for each connection in this pool.</summary>
    IPoolFactory SetSocketBufferSize(int socketBufferSize);

    /// <summary>How long to wait for a server response before failing over to another server.</summary>
    IPoolFactory SetReadTimeout(TimeSpan readTimeout);

    /// <summary>Minimum connections the pool keeps open (warmed up at init; floor for idle cleanup).</summary>
    IPoolFactory SetMinConnections(int minConnections);

    /// <summary>Upper cap on pool size; <c>-1</c> means unbounded.</summary>
    IPoolFactory SetMaxConnections(int maxConnections);

    /// <summary>How long a connection may sit unused before the pool closes it back toward <see cref="SetMinConnections"/>.</summary>
    IPoolFactory SetIdleTimeout(TimeSpan idleTimeout);

    /// <summary>Failover retry budget per op; <c>-1</c> means try every available server before failing.</summary>
    IPoolFactory SetRetryAttempts(int retryAttempts);

    /// <summary>Frequency at which idle servers are pinged to keep the client visible.</summary>
    IPoolFactory SetPingInterval(TimeSpan pingInterval);

    /// <summary>How often the pool refreshes the locator set from an active locator; <see cref="TimeSpan.Zero"/> disables the loop.</summary>
    IPoolFactory SetUpdateLocatorListInterval(TimeSpan updateLocatorListInterval);

    /// <summary>Frequency at which client statistics are sent to the server; <see cref="TimeSpan.Zero"/> disables sending.</summary>
    IPoolFactory SetStatisticInterval(TimeSpan statisticInterval);

    /// <summary>Logical group of servers this pool targets; empty string means all servers.</summary>
    IPoolFactory SetServerGroup(string serverGroup);

    /// <summary>Enable server-to-client subscription on this pool.</summary>
    IPoolFactory SetSubscriptionEnabled(bool subscriptionEnabled);

    /// <summary>Redundancy level for servers holding subscriptions established by this client.</summary>
    IPoolFactory SetSubscriptionRedundancy(int subscriptionRedundancy);

    /// <summary>How long subscription messages from a server are tracked to deduplicate events.</summary>
    IPoolFactory SetSubscriptionMessageTrackingTimeout(TimeSpan subscriptionMessageTrackingTimeout);

    /// <summary>How long to batch subscription event acks before sending to the server.</summary>
    IPoolFactory SetSubscriptionAckInterval(TimeSpan subscriptionAckInterval);

    /// <summary>When <see langword="true"/>, each thread caches its own pool connection (trades server load for client thread contention).</summary>
    IPoolFactory SetThreadLocalConnection(bool threadLocalConnection);

    /// <summary>Enable multi-user secure mode (each authenticated user holds its own server-side proxy).</summary>
    IPoolFactory SetMultiuserSecureMode(bool multiuserSecureMode);

    /// <summary>Enable partitioned-region single-hop routing (ops go directly to the bucket primary).</summary>
    IPoolFactory SetPrSingleHopEnabled(bool prSingleHopEnabled);

    /// <summary>TLS SNI proxy host, when servers are fronted by an SNI-capable proxy.</summary>
    IPoolFactory SetSniProxyHost(string sniProxyHost);

    /// <summary>TLS SNI proxy port; paired with <see cref="SetSniProxyHost"/>.</summary>
    IPoolFactory SetSniProxyPort(int sniProxyPort);

    /// <summary>Append a locator endpoint; a pool may hold locators or servers, not both.</summary>
    /// <exception cref="ArgumentException">A server has already been added.</exception>
    IPoolFactory AddLocator(string host, int port);

    /// <summary>Append a direct server endpoint; a pool may hold locators or servers, not both.</summary>
    /// <exception cref="ArgumentException">A locator has already been added.</exception>
    IPoolFactory AddServer(string host, int port);

    /// <summary>Validate, snapshot, register, and connect a new pool under <paramref name="poolName"/>.</summary>
    /// <exception cref="OptionsValidationException">Current attributes failed validation.</exception>
    /// <exception cref="InvalidOperationException">A pool is already registered under <paramref name="poolName"/>.</exception>
    Task<IPool> BuildAsync(string poolName, CancellationToken ct = default);
}
