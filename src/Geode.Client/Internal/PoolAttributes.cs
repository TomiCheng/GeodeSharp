namespace Geode.Client.Internal;

/// <summary>
/// Pool configuration bag held by <see cref="PoolFactory"/>. Snapshotted via
/// <see cref="Clone"/> at <see cref="PoolFactory.Build"/> time and handed to
/// the built pool, so further factory mutations don't affect already-built pools.
/// </summary>
/// <remarks>
/// Mirrors cppcache <c>PoolAttributes</c> (<c>cppcache/src/PoolAttributes.hpp</c>)
/// 1:1. Defaults match <c>PoolFactory::DEFAULT_*</c> constants in
/// <c>cppcache/include/geode/PoolFactory.hpp</c> + <c>cppcache/src/PoolFactory.cpp</c>.
/// </remarks>
internal sealed class PoolAttributes
{
    // PoolFactory.cpp:35-36 — std::chrono::seconds{10}
    public TimeSpan FreeConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // PoolFactory.cpp:38-39 — std::chrono::minutes{5}
    public TimeSpan LoadConditioningInterval { get; set; } = TimeSpan.FromMinutes(5);

    // PoolFactory.hpp:90 — DEFAULT_SOCKET_BUFFER_SIZE = 32768
    public int SocketBufferSize { get; set; } = 32768;

    // PoolFactory.cpp:41-42 — std::chrono::seconds{10}
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

    // PoolFactory.hpp:102 — DEFAULT_MIN_CONNECTIONS = 1
    public int MinConnections { get; set; } = 1;

    // PoolFactory.hpp:108 — DEFAULT_MAX_CONNECTIONS = -1 (unbounded)
    public int MaxConnections { get; set; } = -1;

    // PoolFactory.cpp:44-45 — std::chrono::seconds{5}
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(5);

    // PoolFactory.hpp:121 — DEFAULT_RETRY_ATTEMPTS = -1 (pool decides)
    public int RetryAttempts { get; set; } = -1;

    // PoolFactory.cpp:47-48 — std::chrono::seconds{10}
    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);

    // PoolFactory.cpp:50-51 — std::chrono::seconds{5}
    public TimeSpan UpdateLocatorListInterval { get; set; } = TimeSpan.FromSeconds(5);

    // PoolFactory.cpp:53-54 — milliseconds::zero() (disabled)
    public TimeSpan StatisticInterval { get; set; } = TimeSpan.Zero;

    // PoolFactory.hpp:146 — DEFAULT_SUBSCRIPTION_ENABLED = false
    public bool SubscriptionEnabled { get; set; }

    // PoolFactory.hpp:154 — DEFAULT_SUBSCRIPTION_REDUNDANCY = 0
    public int SubscriptionRedundancy { get; set; }

    // PoolFactory.cpp:56-58 — std::chrono::seconds{900}
    public TimeSpan SubscriptionMessageTrackingTimeout { get; set; } = TimeSpan.FromSeconds(900);

    // PoolFactory.cpp:60-61 — std::chrono::seconds{100}
    public TimeSpan SubscriptionAckInterval { get; set; } = TimeSpan.FromSeconds(100);

    // PoolFactory.hpp:180 — DEFAULT_THREAD_LOCAL_CONN = false
    public bool ThreadLocalConnection { get; set; }

    // PoolFactory.hpp:186 — DEFAULT_MULTIUSER_SECURE_MODE = false
    public bool MultiuserSecureMode { get; set; }

    // PoolFactory.hpp:192 — DEFAULT_PR_SINGLE_HOP_ENABLED = true
    public bool PrSingleHopEnabled { get; set; } = true;

    // PoolFactory.cpp:63 — DEFAULT_SERVER_GROUP = ""
    public string ServerGroup { get; set; } = string.Empty;

    // PoolAttributes.cpp:48 — m_sniProxyPort(0); m_sniProxyHost default empty
    public string SniProxyHost { get; set; } = string.Empty;
    public int SniProxyPort { get; set; }

    public List<HostPort> Locators { get; } = [];
    public List<HostPort> Servers { get; } = [];

    /// <summary>
    /// Append a locator. Mirrors cppcache <c>PoolAttributes::addLocator</c>
    /// (<c>PoolAttributes.cpp:71-77</c>): a pool has locators OR servers, not both.
    /// </summary>
    public void AddLocator(string host, int port)
    {
        if (Servers.Count > 0)
        {
            throw new ArgumentException("Cannot add both locators and servers to a pool");
        }
        Locators.Add(new HostPort(host, port));
    }

    /// <summary>
    /// Append a server. Mirrors cppcache <c>PoolAttributes::addServer</c>
    /// (<c>PoolAttributes.cpp:79-85</c>): a pool has locators OR servers, not both.
    /// </summary>
    public void AddServer(string host, int port)
    {
        if (Locators.Count > 0)
        {
            throw new ArgumentException("Cannot add both locators and servers to a pool");
        }
        Servers.Add(new HostPort(host, port));
    }

    /// <summary>
    /// Validate. Same rule set as the prior <c>CachePoolOptions.Validate</c>
    /// (minus the <c>Name</c> check — name is <see cref="PoolFactory.Build"/>'s
    /// concern, not part of the attrs). Adjusted for cppcache integer
    /// sentinels: <see cref="MaxConnections"/> = -1 means unbounded,
    /// <see cref="RetryAttempts"/> = -1 means pool decides.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (Locators.Count + Servers.Count == 0)
            yield return $"{prefix} must have at least one locator or server.";

        // cppcache permits 0 (pure lazy).
        if (MinConnections < 0)
            yield return $"{prefix}.{nameof(MinConnections)} must be >= 0 (got {MinConnections}).";

        // -1 = unbounded; skip comparison.
        if (MaxConnections != -1 && MaxConnections < MinConnections)
            yield return $"{prefix}.{nameof(MaxConnections)} ({MaxConnections}) must be >= {nameof(MinConnections)} ({MinConnections}).";

        // cppcache PoolFactory::setUpdateLocatorListInterval (PoolFactory.cpp):
        // negative rejected; 0 = disable refresh loop.
        if (UpdateLocatorListInterval < TimeSpan.Zero)
            yield return $"{prefix}.{nameof(UpdateLocatorListInterval)} must be >= 0 (got {UpdateLocatorListInterval}).";

        // cppcache PoolFactory::setLoadConditioningInterval: negative rejected;
        // 0 = disable load conditioning.
        if (LoadConditioningInterval < TimeSpan.Zero)
            yield return $"{prefix}.{nameof(LoadConditioningInterval)} must be >= 0 (got {LoadConditioningInterval}).";

        // cppcache PoolFactory::setIdleTimeout: negative rejected;
        // 0 = disable idle-driven shrink.
        if (IdleTimeout < TimeSpan.Zero)
            yield return $"{prefix}.{nameof(IdleTimeout)} must be >= 0 (got {IdleTimeout}).";

        // -1 = pool decides (cppcache DEFAULT_RETRY_ATTEMPTS).
        if (RetryAttempts < -1)
            yield return $"{prefix}.{nameof(RetryAttempts)} must be >= -1 (got {RetryAttempts}).";

        for (var i = 0; i < Locators.Count; i++)
        {
            var l = Locators[i];
            if (string.IsNullOrWhiteSpace(l.Host))
                yield return $"{prefix}.Locators[{i}].Host must not be null, empty, or whitespace.";
            if (l.Port is < 1 or > 65535)
                yield return $"{prefix}.Locators[{i}].Port must be in the range [1, 65535] (got {l.Port}).";
        }

        for (var i = 0; i < Servers.Count; i++)
        {
            var s = Servers[i];
            if (string.IsNullOrWhiteSpace(s.Host))
                yield return $"{prefix}.Servers[{i}].Host must not be null, empty, or whitespace.";
            if (s.Port is < 1 or > 65535)
                yield return $"{prefix}.Servers[{i}].Port must be in the range [1, 65535] (got {s.Port}).";
        }
    }

    /// <summary>Deep clone; snapshot for <see cref="PoolFactory.Build"/>.</summary>
    public PoolAttributes Clone()
    {
        var c = new PoolAttributes
        {
            FreeConnectionTimeout = FreeConnectionTimeout,
            LoadConditioningInterval = LoadConditioningInterval,
            SocketBufferSize = SocketBufferSize,
            ReadTimeout = ReadTimeout,
            MinConnections = MinConnections,
            MaxConnections = MaxConnections,
            IdleTimeout = IdleTimeout,
            RetryAttempts = RetryAttempts,
            PingInterval = PingInterval,
            UpdateLocatorListInterval = UpdateLocatorListInterval,
            StatisticInterval = StatisticInterval,
            SubscriptionEnabled = SubscriptionEnabled,
            SubscriptionRedundancy = SubscriptionRedundancy,
            SubscriptionMessageTrackingTimeout = SubscriptionMessageTrackingTimeout,
            SubscriptionAckInterval = SubscriptionAckInterval,
            ThreadLocalConnection = ThreadLocalConnection,
            MultiuserSecureMode = MultiuserSecureMode,
            PrSingleHopEnabled = PrSingleHopEnabled,
            ServerGroup = ServerGroup,
            SniProxyHost = SniProxyHost,
            SniProxyPort = SniProxyPort,
        };
        c.Locators.AddRange(Locators);
        c.Servers.AddRange(Servers);
        return c;
    }
}

internal readonly record struct HostPort(string Host, int Port);
