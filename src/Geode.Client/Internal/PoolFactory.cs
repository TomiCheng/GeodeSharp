using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Default <see cref="IPoolFactory"/>. Mirrors cppcache <c>PoolFactory</c>
/// (<c>cppcache/include/geode/PoolFactory.hpp</c>); setters mutate the
/// internal <see cref="PoolAttributes"/>; <see cref="BuildAsync"/> snapshots
/// them so further factory mutations don't affect already-built pools.
/// </summary>
internal sealed class PoolFactory : IPoolFactory
{
    private PoolAttributes _attrs = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly PoolManager _poolManager;

    // Public so ActivatorUtilities (used by PoolManager.CreateFactory) can
    // see it. The type itself is internal — external callers only ever see
    // it through the IPoolFactory interface returned by IPoolManager.
    public PoolFactory(
        IServiceProvider serviceProvider,
        PoolManager poolManager)
    {
        _serviceProvider = serviceProvider;
        _poolManager = poolManager;
    }

    public IPoolFactory Reset()
    {
        _attrs = new();
        return this;
    }

    public IPoolFactory SetFreeConnectionTimeout(TimeSpan connectionTimeout)
    {
        _attrs.FreeConnectionTimeout = connectionTimeout;
        return this;
    }

    public IPoolFactory SetLoadConditioningInterval(TimeSpan loadConditioningInterval)
    {
        _attrs.LoadConditioningInterval = loadConditioningInterval;
        return this;
    }

    public IPoolFactory SetSocketBufferSize(int socketBufferSize)
    {
        _attrs.SocketBufferSize = socketBufferSize;
        return this;
    }

    public IPoolFactory SetReadTimeout(TimeSpan readTimeout)
    {
        _attrs.ReadTimeout = readTimeout;
        return this;
    }

    public IPoolFactory SetMinConnections(int minConnections)
    {
        _attrs.MinConnections = minConnections;
        return this;
    }

    public IPoolFactory SetMaxConnections(int maxConnections)
    {
        _attrs.MaxConnections = maxConnections;
        return this;
    }

    public IPoolFactory SetIdleTimeout(TimeSpan idleTimeout)
    {
        _attrs.IdleTimeout = idleTimeout;
        return this;
    }

    public IPoolFactory SetRetryAttempts(int retryAttempts)
    {
        _attrs.RetryAttempts = retryAttempts;
        return this;
    }

    public IPoolFactory SetPingInterval(TimeSpan pingInterval)
    {
        _attrs.PingInterval = pingInterval;
        return this;
    }

    public IPoolFactory SetUpdateLocatorListInterval(TimeSpan updateLocatorListInterval)
    {
        _attrs.UpdateLocatorListInterval = updateLocatorListInterval;
        return this;
    }

    public IPoolFactory SetStatisticInterval(TimeSpan statisticInterval)
    {
        _attrs.StatisticInterval = statisticInterval;
        return this;
    }

    public IPoolFactory SetServerGroup(string serverGroup)
    {
        _attrs.ServerGroup = serverGroup;
        return this;
    }

    public IPoolFactory SetSubscriptionEnabled(bool subscriptionEnabled)
    {
        _attrs.SubscriptionEnabled = subscriptionEnabled;
        return this;
    }

    public IPoolFactory SetSubscriptionRedundancy(int subscriptionRedundancy)
    {
        _attrs.SubscriptionRedundancy = subscriptionRedundancy;
        return this;
    }

    public IPoolFactory SetSubscriptionMessageTrackingTimeout(TimeSpan subscriptionMessageTrackingTimeout)
    {
        _attrs.SubscriptionMessageTrackingTimeout = subscriptionMessageTrackingTimeout;
        return this;
    }

    public IPoolFactory SetSubscriptionAckInterval(TimeSpan subscriptionAckInterval)
    {
        _attrs.SubscriptionAckInterval = subscriptionAckInterval;
        return this;
    }

    public IPoolFactory SetThreadLocalConnection(bool threadLocalConnection)
    {
        _attrs.ThreadLocalConnection = threadLocalConnection;
        return this;
    }

    public IPoolFactory SetMultiuserSecureMode(bool multiuserSecureMode)
    {
        _attrs.MultiuserSecureMode = multiuserSecureMode;
        return this;
    }

    public IPoolFactory SetPrSingleHopEnabled(bool prSingleHopEnabled)
    {
        _attrs.PrSingleHopEnabled = prSingleHopEnabled;
        return this;
    }

    public IPoolFactory SetSniProxyHost(string sniProxyHost)
    {
        _attrs.SniProxyHost = sniProxyHost;
        return this;
    }

    public IPoolFactory SetSniProxyPort(int sniProxyPort)
    {
        _attrs.SniProxyPort = sniProxyPort;
        return this;
    }

    public IPoolFactory AddLocator(string host, int port)
    {
        _attrs.AddLocator(host, port);
        return this;
    }

    public IPoolFactory AddServer(string host, int port)
    {
        _attrs.AddServer(host, port);
        return this;
    }

    public async Task<IPool> BuildAsync(string poolName, CancellationToken ct = default)
    {
        var errors = _attrs.Validate(nameof(PoolAttributes)).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"{nameof(PoolAttributes)} validation failed: {string.Join("; ", errors)}");
        }

        var snapshot = _attrs.Clone();
        // ThinClientPoolDM ctor takes (sp, name, PoolAttributes) — sp comes
        // from ActivatorUtilities itself; _poolManager is NOT a ctor param
        // (it's resolved via DI inside the type if needed).
        var pool = ActivatorUtilities.CreateInstance<ThinClientPoolDM>(_serviceProvider, poolName, snapshot);
        _poolManager.AddPool(poolName, pool);
        await pool.InitAsync(ct).ConfigureAwait(false);
        return pool;
    }
}
