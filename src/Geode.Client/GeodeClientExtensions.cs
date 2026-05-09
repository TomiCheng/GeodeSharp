using Geode.Client.Options;
using Geode.Client.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client;

/// <summary>
/// DI registration entry point for the Geode managed client.
/// </summary>
public static class GeodeClientExtensions
{
    /// <summary>
    /// Default <c>IConfiguration</c> section name bound by the
    /// parameterless <see cref="AddGeodeClient(IServiceCollection)"/>
    /// overload.
    /// </summary>
    public const string DefaultSectionName = "Geode";

    /// <summary>
    /// Register the Geode client and bind
    /// <see cref="GeodeClientOptions"/> from the application's
    /// <see cref="IConfiguration"/> using the default section name
    /// <c>"Geode"</c>. <see cref="IConfiguration"/> is resolved from
    /// the container at options-build time.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGeodeClient();
    /// </code>
    /// </example>
    public static IServiceCollection AddGeodeClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<GeodeClientOptions>().BindConfiguration(DefaultSectionName);
        return AddGeodeClientCore(services);
    }

    /// <summary>
    /// Register the Geode client and bind
    /// <see cref="GeodeClientOptions"/> from the supplied
    /// <paramref name="configuration"/>. Pass either the root
    /// configuration (binding will pick up nothing unless the keys are
    /// at the root) or — more usually — a sub-section such as
    /// <c>builder.Configuration.GetSection("Geode")</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddGeodeClient(
    ///     builder.Configuration.GetSection("Geode"));
    /// </code>
    /// </example>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<GeodeClientOptions>().Bind(configuration);
        return AddGeodeClientCore(services);
    }

    /// <summary>
    /// Register the Geode client and configure
    /// <see cref="GeodeClientOptions"/> programmatically. Useful for
    /// tests, hosts without a configuration provider, or callers who
    /// want compile-time control over option values.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddGeodeClient(opt =>
    /// {
    ///     opt.Name = "order-service";
    ///     opt.Pool.PingInterval = TimeSpan.FromSeconds(5);
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        Action<GeodeClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GeodeClientOptions>().Configure(configure);
        return AddGeodeClientCore(services);
    }

    /// <summary>
    /// Shared service-registration body. Each public overload sets up
    /// options binding its own way then delegates here.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="ClientProxyMembershipIdBuilder"/> as a
    ///     <b>singleton</b> — process-scoped uniqueTag and
    ///     identity-bytes cache must be shared across all connections.
    ///   </item>
    ///   <item>
    ///     <see cref="TcrConnection"/> as <b>transient</b> — every
    ///     borrow yields a fresh connection. The pool implementation
    ///     will replace this with a pooled lifetime once it lands.
    ///   </item>
    /// </list>
    /// <para>
    /// Logging is intentionally not registered here; callers are
    /// expected to add their own <c>ILoggerFactory</c> via
    /// <c>AddLogging()</c> / Serilog / etc.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddGeodeClientCore(IServiceCollection services)
    {
        services.AddSingleton<ClientProxyMembershipIdBuilder>();
        services.AddSingleton<TcrPartBuilder>();
        services.AddSingleton<TcrMessageBuilder>();
        services.AddTransient<TcrConnection>();

        return services;
    }
}
