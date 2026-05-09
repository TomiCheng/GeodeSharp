namespace Geode.Client.Options;

/// <summary>
/// User-facing configuration for the Geode client. Bound from the
/// <c>"Geode"</c> section of <c>appsettings.json</c> via
/// <c>IOptions&lt;GeodeClientOptions&gt;</c> and consumed by the (Phase 5)
/// <c>AddGeodeClient(...)</c> DI extension.
/// </summary>
/// <remarks>
/// <para>
/// Property set is derived from cppcache <c>SystemProperties</c> (file
/// <c>cppcache/include/geode/SystemProperties.hpp</c> + defaults in
/// <c>cppcache/src/SystemProperties.cpp</c>). The following cppcache
/// fields are intentionally <b>omitted</b> because the .NET runtime /
/// our architecture replaces them:
/// </para>
/// <list type="bullet">
///   <item>statistic-* (use <c>EventCounters</c> / OpenTelemetry).</item>
///   <item>log-* (use <c>ILogger</c> + filter levels).</item>
///   <item>heap-lru-* / tombstone-timeout (server-side concepts).</item>
///   <item>suspended-tx-timeout / bucket-wait-timeout (out of MVP scope).</item>
///   <item>max-fe-threads / enable-chunk-handler-thread (.NET ThreadPool managed).</item>
///   <item>security-client-dhalgo (Diffie-Hellman creds — deprecated upstream).</item>
///   <item>on-client-disconnect-clear-pdxType-Ids (Phase 11 PDX).</item>
///   <item>cache-xml-file (CLAUDE.md cuts <c>cache.xml</c> entirely).</item>
/// </list>
/// </remarks>
public class GeodeClientOptions
{
    /// <summary>
    /// Distributed-system / client name shown in server logs. Mirrors
    /// cppcache <c>name</c>. Default empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Connection-pool tuning. See <see cref="PoolOptions"/>.</summary>
    public PoolOptions Pool { get; } = new();

    /// <summary>TLS / SSL settings. See <see cref="TlsOptions"/>.</summary>
    public TlsOptions Tls { get; } = new();

    /// <summary>
    /// Subscription / durable-client / event-notification settings.
    /// See <see cref="SubscriptionOptions"/>.
    /// </summary>
    public SubscriptionOptions Subscription { get; } = new();
}
