using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geode.Client;

/// <summary>
/// Fluent builder for connection pools; obtain via <see cref="IPoolManager.CreateFactory"/>.
/// </summary>
/// <remarks>
/// Mirrors cppcache <c>PoolFactory</c> (<c>cppcache/include/geode/PoolFactory.hpp</c>).
/// Setters mutate the internal <see cref="PoolAttributes"/>; <see cref="Build"/>
/// snapshots them so further factory mutations don't affect already-built pools.
/// </remarks>
public class PoolFactory
{
    private PoolAttributes _attrs = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly PoolManager _poolManager;

    internal PoolFactory(
        IServiceProvider serviceProvider,
        PoolManager poolManager)
    {
        _serviceProvider = serviceProvider;
        _poolManager = poolManager;
    }

    /// <summary>Reset all attributes back to defaults.</summary>
    public PoolFactory Reset()
    {
        _attrs = new();
        return this;
    }

    /// <summary>How long an op may wait for a free connection when the pool has hit its max.</summary>
    public PoolFactory SetFreeConnectionTimeout(TimeSpan connectionTimeout)
    {
        _attrs.FreeConnectionTimeout = connectionTimeout;
        return this;
    }

    /// <summary>How often connections are rotated to rebalance load across the server cluster.</summary>
    public PoolFactory SetLoadConditioningInterval(TimeSpan loadConditioningInterval)
    {
        _attrs.LoadConditioningInterval = loadConditioningInterval;
        return this;
    }

    /// <summary>TCP send/receive buffer size in bytes for each connection in this pool.</summary>
    public PoolFactory SetSocketBufferSize(int socketBufferSize)
    {
        _attrs.SocketBufferSize = socketBufferSize;
        return this;
    }

    /// <summary>How long to wait for a server response before failing over to another server.</summary>
    public PoolFactory SetReadTimeout(TimeSpan readTimeout)
    {
        _attrs.ReadTimeout = readTimeout;
        return this;
    }

    /// <summary>Minimum connections the pool keeps open (warmed up at init; floor for idle cleanup).</summary>
    public PoolFactory SetMinConnections(int minConnections)
    {
        _attrs.MinConnections = minConnections;
        return this;
    }

    /// <summary>Upper cap on pool size; <c>-1</c> means unbounded.</summary>
    public PoolFactory SetMaxConnections(int maxConnections)
    {
        _attrs.MaxConnections = maxConnections;
        return this;
    }

    /// <summary>How long a connection may sit unused before the pool closes it back toward <see cref="SetMinConnections"/>.</summary>
    public PoolFactory SetIdleTimeout(TimeSpan idleTimeout)
    {
        _attrs.IdleTimeout = idleTimeout;
        return this;
    }

    /// <summary>Failover retry budget per op; <c>-1</c> means try every available server before failing.</summary>
    public PoolFactory SetRetryAttempts(int retryAttempts)
    {
        _attrs.RetryAttempts = retryAttempts;
        return this;
    }

    /// <summary>Frequency at which idle servers are pinged to keep the client visible.</summary>
    public PoolFactory SetPingInterval(TimeSpan pingInterval)
    {
        _attrs.PingInterval = pingInterval;
        return this;
    }

    /// <summary>How often the pool refreshes the locator set from an active locator; <see cref="TimeSpan.Zero"/> disables the loop.</summary>
    public PoolFactory SetUpdateLocatorListInterval(TimeSpan updateLocatorListInterval)
    {
        _attrs.UpdateLocatorListInterval = updateLocatorListInterval;
        return this;
    }

    /// <summary>Frequency at which client statistics are sent to the server; <see cref="TimeSpan.Zero"/> disables sending.</summary>
    public PoolFactory SetStatisticInterval(TimeSpan statisticInterval)
    {
        _attrs.StatisticInterval = statisticInterval;
        return this;
    }

    /// <summary>Logical group of servers this pool targets; empty string means all servers.</summary>
    public PoolFactory SetServerGroup(string serverGroup)
    {
        _attrs.ServerGroup = serverGroup;
        return this;
    }

    /// <summary>Enable server-to-client subscription on this pool.</summary>
    public PoolFactory SetSubscriptionEnabled(bool subscriptionEnabled)
    {
        _attrs.SubscriptionEnabled = subscriptionEnabled;
        return this;
    }

    /// <summary>Redundancy level for servers holding subscriptions established by this client.</summary>
    public PoolFactory SetSubscriptionRedundancy(int subscriptionRedundancy)
    {
        _attrs.SubscriptionRedundancy = subscriptionRedundancy;
        return this;
    }

    /// <summary>How long subscription messages from a server are tracked to deduplicate events.</summary>
    public PoolFactory SetSubscriptionMessageTrackingTimeout(TimeSpan subscriptionMessageTrackingTimeout)
    {
        _attrs.SubscriptionMessageTrackingTimeout = subscriptionMessageTrackingTimeout;
        return this;
    }

    /// <summary>How long to batch subscription event acks before sending to the server.</summary>
    public PoolFactory SetSubscriptionAckInterval(TimeSpan subscriptionAckInterval)
    {
        _attrs.SubscriptionAckInterval = subscriptionAckInterval;
        return this;
    }

    /// <summary>When <see langword="true"/>, each thread caches its own pool connection (trades server load for client thread contention).</summary>
    public PoolFactory SetThreadLocalConnection(bool threadLocalConnection)
    {
        _attrs.ThreadLocalConnection = threadLocalConnection;
        return this;
    }

    /// <summary>Enable multi-user secure mode (each authenticated user holds its own server-side proxy).</summary>
    public PoolFactory SetMultiuserSecureMode(bool multiuserSecureMode)
    {
        _attrs.MultiuserSecureMode = multiuserSecureMode;
        return this;
    }

    /// <summary>Enable partitioned-region single-hop routing (ops go directly to the bucket primary).</summary>
    public PoolFactory SetPrSingleHopEnabled(bool prSingleHopEnabled)
    {
        _attrs.PrSingleHopEnabled = prSingleHopEnabled;
        return this;
    }

    /// <summary>TLS SNI proxy host, when servers are fronted by an SNI-capable proxy.</summary>
    public PoolFactory SetSniProxyHost(string sniProxyHost)
    {
        _attrs.SniProxyHost = sniProxyHost;
        return this;
    }

    /// <summary>TLS SNI proxy port; paired with <see cref="SetSniProxyHost"/>.</summary>
    public PoolFactory SetSniProxyPort(int sniProxyPort)
    {
        _attrs.SniProxyPort = sniProxyPort;
        return this;
    }

    /// <summary>Append a locator endpoint; a pool may hold locators or servers, not both.</summary>
    /// <exception cref="ArgumentException">A server has already been added.</exception>
    public PoolFactory AddLocator(string host, int port)
    {
        _attrs.AddLocator(host, port);
        return this;
    }

    /// <summary>Append a direct server endpoint; a pool may hold locators or servers, not both.</summary>
    /// <exception cref="ArgumentException">A locator has already been added.</exception>
    public PoolFactory AddServer(string host, int port)
    {
        _attrs.AddServer(host, port);
        return this;
    }

    /// <summary>Validate, snapshot, register, and connect a new pool under <paramref name="poolName"/>.</summary>
    /// <exception cref="OptionsValidationException">Current attributes failed validation.</exception>
    /// <exception cref="InvalidOperationException">A pool is already registered under <paramref name="poolName"/>.</exception>
    public async Task<IPool> BuildAsync(string poolName, CancellationToken ct = default)
    {
        var errors = _attrs.Validate(nameof(PoolAttributes)).ToList();
        if (errors.Count > 0)
        {
            throw new OptionsValidationException(
                nameof(PoolAttributes), typeof(PoolAttributes), errors);
        }

        var snapshot = _attrs.Clone();
        var pool = ActivatorUtilities.CreateInstance<ThinClientPoolDM>(_serviceProvider, _poolManager, poolName, snapshot);
        _poolManager.AddPool(poolName, pool);
        await pool.InitAsync(ct).ConfigureAwait(false);
        return pool;
    }
}
