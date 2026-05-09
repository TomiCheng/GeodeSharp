using Geode.Client.Options;
using Geode.Client.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client;

/// <summary>
/// DI registration entry point for the Geode managed client.
/// </summary>
public static class GeodeClientServiceCollectionExtensions
{
    /// <summary>
    /// Register the Geode client services and bind
    /// <see cref="GeodeClientOptions"/> from
    /// <paramref name="configuration"/> (typically the <c>"Geode"</c>
    /// section of <c>appsettings.json</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGeodeClient(
    ///     builder.Configuration.GetSection("Geode"));
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// Phase 2 surface — registers the bare minimum needed to open and
    /// handshake a single connection:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="GeodeClientOptions"/> bound from configuration.</item>
    ///   <item>
    ///     <see cref="ClientProxyMembershipIdBuilder"/> as a <b>singleton</b>
    ///     — process-scoped uniqueTag and identity-bytes cache must be
    ///     shared across all connections.
    ///   </item>
    ///   <item>
    ///     <see cref="TcrConnection"/> as <b>transient</b> — every borrow
    ///     yields a fresh connection. Phase 6 will replace this with a
    ///     pooled lifetime.
    ///   </item>
    /// </list>
    /// <para>
    /// Logging is intentionally not registered here; callers are expected
    /// to add their own <c>ILoggerFactory</c> via <c>AddLogging()</c> /
    /// <c>AddHttpLogging()</c> / Serilog / etc. so the Geode client picks
    /// up whatever logging stack the host already configured.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GeodeClientOptions>().Bind(configuration);
        services.AddSingleton<ClientProxyMembershipIdBuilder>();
        services.AddTransient<TcrConnection>();

        return services;
    }
}
