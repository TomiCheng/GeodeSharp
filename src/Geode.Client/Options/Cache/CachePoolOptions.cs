namespace Geode.Client.Options;

/// <summary>
/// Mirrors a <c>&lt;pool&gt;</c> element from <c>cache.xml</c>. Distinct
/// from <see cref="PoolOptions"/> (which mirrors the global
/// <c>SystemProperties</c> pool defaults) — this one represents a
/// <b>named</b> pool that regions reference via
/// <see cref="CacheRegionAttributesOptions.PoolName"/>.
/// </summary>
/// <remarks>
/// All attributes are nullable to preserve "not set in XML" vs
/// "explicitly set" — when the field is null, cppcache falls back to its
/// <see cref="PoolOptions"/>-equivalent global default.
/// </remarks>
public class CachePoolOptions : ICloneable
{

    public CachePoolOptions() { }

    public CachePoolOptions(CachePoolOptions other)
    {
        Name = other.Name;
        FreeConnectionTimeout = other.FreeConnectionTimeout;
        LoadConditioningInterval = other.LoadConditioningInterval;
        MinConnections = other.MinConnections;
        MaxConnections = other.MaxConnections;
        RetryAttempts = other.RetryAttempts;
        IdleTimeout = other.IdleTimeout;
        PingInterval = other.PingInterval;
        ReadTimeout = other.ReadTimeout;
        ServerGroup = other.ServerGroup;
        SocketBufferSize = other.SocketBufferSize;
        SubscriptionEnabled = other.SubscriptionEnabled;
        SubscriptionMessageTrackingTimeout = other.SubscriptionMessageTrackingTimeout;
        SubscriptionAckInterval = other.SubscriptionAckInterval;
        SubscriptionRedundancy = other.SubscriptionRedundancy;
        StatisticInterval = other.StatisticInterval;
        PrSingleHopEnabled = other.PrSingleHopEnabled;
        ThreadLocalConnections = other.ThreadLocalConnections;
        MultiuserAuthentication = other.MultiuserAuthentication;
        UpdateLocatorListInterval = other.UpdateLocatorListInterval;
        Locators = [.. other.Locators.Select(h => h.Clone())];
        Servers = [.. other.Servers.Select(h => h.Clone())];
    }

    object ICloneable.Clone() => Clone();

    /// <summary>Deep clone via copy constructor.</summary>
    public CachePoolOptions Clone() => new(this);

    /// <summary>
    /// Validate. Rules migrated from <c>GeodeClientOptionsValidator</c>:
    /// <see cref="Name"/> non-empty; at least one locator or server entry;
    /// <see cref="MinConnections"/> &gt;= 0; <see cref="MaxConnections"/>
    /// (when set) &gt;= <see cref="MinConnections"/>. Recurses into each
    /// <see cref="CacheHostPortOptions"/>.
    /// </summary>
    public IEnumerable<string> Validate(string prefix)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return $"{prefix}.Name must not be null, empty, or whitespace.";

        if (Locators.Count + Servers.Count == 0)
            yield return $"{prefix} must have at least one locator or server.";

        // MinConnections == 0 is allowed (cppcache permits 0 = pure lazy).
        if (MinConnections < 0)
            yield return $"{prefix}.MinConnections must be >= 0 (got {MinConnections}).";

        // MaxConnections == null means "unbounded" — skip the comparison.
        if (MaxConnections is int max && max < MinConnections)
            yield return $"{prefix}.MaxConnections ({max}) must be >= MinConnections ({MinConnections}).";

        // Mirrors cppcache PoolFactory::setUpdateLocatorListInterval guard
        // (PoolFactory.cpp:150): negative durations are rejected; 0 is
        // allowed and means "disable the refresh loop".
        if (UpdateLocatorListInterval < TimeSpan.Zero)
            yield return $"{prefix}.UpdateLocatorListInterval must be >= 0 (got {UpdateLocatorListInterval}).";

        // Mirrors cppcache PoolFactory::setLoadConditioningInterval
        // (PoolFactory.cpp:83-86): negative durations are rejected with
        // IllegalArgumentException; 0 = disable load conditioning.
        if (LoadConditioningInterval < TimeSpan.Zero)
            yield return $"{prefix}.LoadConditioningInterval must be >= 0 (got {LoadConditioningInterval}).";

        // Mirrors cppcache PoolFactory::setIdleTimeout
        // (PoolFactory.cpp same pattern): negative durations are rejected;
        // 0 = disable idle-driven shrink (load conditioning takes over).
        if (IdleTimeout < TimeSpan.Zero)
            yield return $"{prefix}.IdleTimeout must be >= 0 (got {IdleTimeout}).";

        for (var i = 0; i < Locators.Count; i++)
        {
            foreach (var f in Locators[i].Validate($"{prefix}.Locators[{i}]"))
                yield return f;
        }

        for (var i = 0; i < Servers.Count; i++)
        {
            foreach (var f in Servers[i].Validate($"{prefix}.Servers[{i}]"))
                yield return f;
        }
    }

    /// <summary>
    /// How long an op may wait for an idle connection when the pool has
    /// reached <see cref="MaxConnections"/>; throws <see cref="AllConnectionsInUseException"/> on timeout.
    /// </summary>
    /// <remarks>
    /// default 10s; must be &gt; <see cref="TimeSpan.Zero"/>.
    /// </remarks>
    public TimeSpan FreeConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a connection can sit unused before the pool may close it
    /// to shrink back toward <see cref="MinConnections"/>.
    /// </summary>
    /// <remarks>
    /// default 10s.
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long before a connection is forcibly rotated to spread
    /// load across the server cluster, independent of idle status.
    /// </summary>
    /// <remarks>
    /// default 5min; <see cref="TimeSpan.Zero"/> disables load conditioning.
    /// </remarks>
    public TimeSpan LoadConditioningInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Pool must have at least one of <see cref="Locators"/> or <see cref="Servers"/> per.
    /// </summary>
    public List<CacheHostPortOptions> Locators { get; set; } = [];

    /// <summary>
    /// Upper cap on pool size; new connection opens are rejected with
    /// <see cref="AllConnectionsInUseException"/> once the pool reaches
    /// this size.
    /// </summary>
    /// <remarks>
    /// default <see langword="null"/> = unbounded.
    /// </remarks>
    public int? MaxConnections { get; set; }

    /// <summary>
    /// Minimum number of connections the pool keeps open; warmed up at init
    /// and treated as a floor when cleaning up idle connections.
    /// </summary>
    /// <remarks>
    /// default 1; <c>0</c> = pure lazy (open on demand only).
    /// </remarks>
    public int MinConnections { get; set; } = 1;

    /// <summary>
    /// <c>multiuser-authentication</c>.
    /// </summary>
    public bool? MultiuserAuthentication { get; set; }

    /// <summary>
    /// Pool identifier; required. Regions reference it via
    /// <see cref="CacheRegionAttributesOptions.PoolName"/>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Same concept as <see cref="PoolOptions.PingInterval"/>.
    /// </summary>
    public TimeSpan? PingInterval { get; set; }

    /// <summary>
    /// <c>pr-single-hop-enabled</c>.
    /// </summary>
    public bool? PrSingleHopEnabled { get; set; }

    /// <summary>
    /// <c>read-timeout</c>.
    /// </summary>
    public TimeSpan? ReadTimeout { get; set; }

    /// <summary>
    /// <c>retry-attempts</c>.
    /// </summary>
    public int? RetryAttempts { get; set; }

    /// <summary>
    /// Logical group of servers this pool targets.
    /// </summary>
    public string ServerGroup { get; set; } = string.Empty;

    /// <summary>
    /// Direct server endpoints for pools that bypass locators.
    /// </summary>
    public List<CacheHostPortOptions> Servers { get; set; } = [];

    /// <summary>
    /// <c>socket-buffer-size</c>. Same concept as
    /// <see cref="PoolOptions.MaxSocketBufferSize"/>.
    /// </summary>
    public int? SocketBufferSize { get; set; }

    /// <summary>
    /// <c>statistic-interval</c>.
    /// </summary>
    public TimeSpan? StatisticInterval { get; set; }

    /// <summary>
    /// <c>subscription-ack-interval</c>. XSD types this as
    /// string but cppcache parses as ms.
    /// </summary>
    public int? SubscriptionAckInterval { get; set; }

    /// <summary>
    /// <c>subscription-enabled</c>.
    /// </summary>
    public bool? SubscriptionEnabled { get; set; }

    /// <summary>
    /// <c>subscription-message-tracking-timeout</c>.
    /// </summary>
    public int? SubscriptionMessageTrackingTimeout { get; set; }

    /// <summary>
    /// <c>subscription-redundancy</c>.
    /// </summary>
    public int? SubscriptionRedundancy { get; set; }

    /// <summary>
    /// <c>thread-local-connections</c>.
    /// </summary>
    public bool? ThreadLocalConnections { get; set; }

    /// <summary>
    /// How often the pool asks an active locator for the current locator set,
    /// so it can pick up newly-added locators and drop dead ones without a client restart;
    /// </summary>
    /// <remarks>
    /// default 5s; <see cref="TimeSpan.Zero"/>disables the refresh loop.
    /// </remarks>
    public TimeSpan UpdateLocatorListInterval { get; set; } = TimeSpan.FromSeconds(5);

}
