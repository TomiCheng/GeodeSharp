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
        Locators = other.Locators.Select(h => h.Clone()).ToList();
        Servers = other.Servers.Select(h => h.Clone()).ToList();
    }

    /// <summary><c>name</c> attribute (required). Region's
    /// <c>pool-name</c> references this.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>free-connection-timeout</c>.</summary>
    public TimeSpan? FreeConnectionTimeout { get; set; }

    /// <summary><c>load-conditioning-interval</c>.</summary>
    public TimeSpan? LoadConditioningInterval { get; set; }

    /// <summary><c>min-connections</c>.</summary>
    public int MinConnections { get; set; } = 1;

    /// <summary><c>max-connections</c>.</summary>
    public int? MaxConnections { get; set; }

    /// <summary><c>retry-attempts</c>.</summary>
    public int? RetryAttempts { get; set; }

    /// <summary><c>idle-timeout</c>.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary><c>ping-interval</c>. Same concept as
    /// <see cref="PoolOptions.PingInterval"/>.</summary>
    public TimeSpan? PingInterval { get; set; }

    /// <summary><c>read-timeout</c>.</summary>
    public TimeSpan? ReadTimeout { get; set; }

    /// <summary><c>server-group</c>. Logical group of servers this pool
    /// targets.</summary>
    public string ServerGroup { get; set; } = string.Empty;

    /// <summary><c>socket-buffer-size</c>. Same concept as
    /// <see cref="PoolOptions.MaxSocketBufferSize"/>.</summary>
    public int? SocketBufferSize { get; set; }

    /// <summary><c>subscription-enabled</c>.</summary>
    public bool? SubscriptionEnabled { get; set; }

    /// <summary><c>subscription-message-tracking-timeout</c>.</summary>
    public int? SubscriptionMessageTrackingTimeout { get; set; }

    /// <summary><c>subscription-ack-interval</c>. XSD types this as
    /// string but cppcache parses as ms.</summary>
    public int? SubscriptionAckInterval { get; set; }

    /// <summary><c>subscription-redundancy</c>.</summary>
    public int? SubscriptionRedundancy { get; set; }

    /// <summary><c>statistic-interval</c>.</summary>
    public TimeSpan? StatisticInterval { get; set; }

    /// <summary><c>pr-single-hop-enabled</c>.</summary>
    public bool? PrSingleHopEnabled { get; set; }

    /// <summary><c>thread-local-connections</c>.</summary>
    public bool? ThreadLocalConnections { get; set; }

    /// <summary><c>multiuser-authentication</c>.</summary>
    public bool? MultiuserAuthentication { get; set; }

    /// <summary><c>update-locator-list-interval</c>.</summary>
    public TimeSpan? UpdateLocatorListInterval { get; set; }

    /// <summary>
    /// <c>&lt;locator&gt;</c> children. Pool must have at least one of
    /// <see cref="Locators"/> or <see cref="Servers"/> per XSD.
    /// </summary>
    public List<CacheHostPortOptions> Locators { get; set; } = new();

    /// <summary>
    /// <c>&lt;server&gt;</c> children. Direct server endpoints for
    /// pools that bypass locators.
    /// </summary>
    public List<CacheHostPortOptions> Servers { get; set; } = new();

    /// <summary>Deep clone via copy constructor.</summary>
    public CachePoolOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

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

        for (var i = 0; i < Locators.Count; i++)
            foreach (var f in Locators[i].Validate($"{prefix}.Locators[{i}]"))
                yield return f;

        for (var i = 0; i < Servers.Count; i++)
            foreach (var f in Servers[i].Validate($"{prefix}.Servers[{i}]"))
                yield return f;
    }
}
