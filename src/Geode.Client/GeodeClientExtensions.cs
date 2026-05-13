using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client;

/// <summary>
/// DI registration entry point for the Geode managed client.
/// </summary>
/// <remarks>
/// <para>Three overloads, each with a trailing optional
/// <paramref name="name"/> for multi-cluster scenarios:</para>
/// <list type="bullet">
///   <item>
///     <see cref="AddGeodeClient(IServiceCollection, string?)"/> —
///     bind from configuration. Section name defaults to
///     <see cref="DefaultSectionName"/> (<c>"Geode"</c>) for the
///     unnamed registration; for named registrations the section name
///     is <paramref name="name"/> itself.
///   </item>
///   <item>
///     <see cref="AddGeodeClient(IServiceCollection, IConfiguration, string?)"/>
///     — bind from a caller-supplied <see cref="IConfiguration"/>.
///   </item>
///   <item>
///     <see cref="AddGeodeClient(IServiceCollection, Action{GeodeClientOptions}, string?)"/>
///     — programmatic configuration.
///   </item>
/// </list>
/// <para>
/// The unnamed registration also exposes <see cref="IGeodeCache"/>
/// directly in the container, so single-cluster callers can inject it
/// without going through <see cref="IGeodeCacheFactory"/>. Named
/// registrations are reachable via
/// <c>IGeodeCacheFactory.Get(name)</c> or
/// <c>[FromKeyedServices(name)] IGeodeCache</c>.
/// </para>
/// <para>
/// <b>Resolution matrix — pick the right injection style for the
/// registration you used:</b>
/// </para>
/// <code>
/// // Unnamed only — single cluster
/// services.AddGeodeClient(cfg.GetSection("Geode"));
/// public class Svc(IGeodeCache cache) { }                                    // OK
/// public class Svc(IGeodeCacheFactory f) { var c = f.Get(); }                // OK
///
/// // Named only — multi-cluster
/// services.AddGeodeClient("g1", cfg.GetSection("g1"));
/// services.AddGeodeClient("g2", cfg.GetSection("g2"));
/// public class Svc(IGeodeCache cache) { }                                    // ✗ throws — no unnamed registration
/// public class Svc(IGeodeCacheFactory f) { var c = f.Get("g1"); }            // OK
/// public class Svc([FromKeyedServices("g1")] IGeodeCache c) { }              // OK
///
/// // Mixed — one default + several named
/// services.AddGeodeClient(cfg.GetSection("Geode"));
/// services.AddGeodeClient("legacy", cfg.GetSection("legacy"));
/// public class Svc(IGeodeCache main,                                         // unnamed default
///                  [FromKeyedServices("legacy")] IGeodeCache legacy) { }     // named
/// </code>
/// <para>
/// If you inject plain <see cref="IGeodeCache"/> but only ever
/// registered named caches, the DI container throws
/// <c>InvalidOperationException</c> with the BCL message
/// <c>"Unable to resolve service for type 'Geode.Client.IGeodeCache'"</c>
/// — switch to <see cref="IGeodeCacheFactory.Get(string)"/> or
/// <c>[FromKeyedServices]</c>, or add an additional unnamed
/// <c>AddGeodeClient(...)</c> registration.
/// </para>
/// </remarks>
public static class GeodeClientExtensions
{
    /// <summary>
    /// Default <see cref="IConfiguration"/> section name for the
    /// unnamed registration overload that takes no
    /// <see cref="IConfiguration"/> argument.
    /// </summary>
    public const string DefaultSectionName = "Geode";

    /// <summary>
    /// Register the Geode client and bind
    /// <see cref="GeodeClientOptions"/> from the host
    /// <see cref="IConfiguration"/>. The section name resolves to
    /// <paramref name="name"/> when supplied, otherwise to
    /// <see cref="DefaultSectionName"/>.
    /// </summary>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var key = name ?? MsOptions.DefaultName;
        var section = name ?? DefaultSectionName;

        services.AddOptions<GeodeClientOptions>(key)
            .BindConfiguration(section)
            .ValidateOnStart();
        return AddCore(services, name);
    }

    /// <summary>
    /// Register the Geode client and bind
    /// <see cref="GeodeClientOptions"/> from
    /// <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var key = name ?? MsOptions.DefaultName;
        services.AddOptions<GeodeClientOptions>(key)
            .Bind(configuration)
            .ValidateOnStart();
        return AddCore(services, name);
    }

    /// <summary>
    /// Register the Geode client and configure
    /// <see cref="GeodeClientOptions"/> programmatically.
    /// </summary>
    public static IServiceCollection AddGeodeClient(
        this IServiceCollection services,
        Action<GeodeClientOptions> configure,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var key = name ?? MsOptions.DefaultName;
        services.AddOptions<GeodeClientOptions>(key)
            .Configure(configure)
            .ValidateOnStart();
        return AddCore(services, name);
    }

    /// <summary>
    /// Shared registration body — singletons that the factory and
    /// every cache instance share, plus the keyed
    /// <see cref="IGeodeCache"/> entry for this <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="ClientProxyMembershipIdBuilder"/> as
    ///     <b>scoped</b> — each cache lives in its own
    ///     <see cref="AsyncServiceScope"/> (created by
    ///     <see cref="GeodeCacheFactory"/>), so a Scoped registration
    ///     gives each cache its own builder. cppcache equivalent
    ///     (<c>ClientProxyMembershipIDFactory</c>) is a per-<c>CacheImpl</c>
    ///     value member, which Scoped here mirrors.
    ///   </item>
    ///   <item>
    ///     <see cref="PoolManager"/> as <b>scoped</b> — same reasoning
    ///     as <see cref="ClientProxyMembershipIdBuilder"/>; each cache
    ///     owns its own pool registry. cppcache equivalent
    ///     (<c>PoolManagerImpl</c>) is held as
    ///     <c>unique_ptr&lt;PoolManager&gt;</c> in <c>CacheImpl</c>,
    ///     which Scoped mirrors.
    ///   </item>
    ///   <item>
    ///     <see cref="IGeodeCacheFactory"/> as a <b>singleton</b> so
    ///     all callers share the same per-name
    ///     <see cref="IGeodeCache"/> instances.
    ///   </item>
    ///   <item>
    ///     Keyed <see cref="IGeodeCache"/> resolves through the
    ///     factory, so <c>[FromKeyedServices]</c> and
    ///     <c>factory.Get(name)</c> return the same object.
    ///   </item>
    ///   <item>
    ///     For the unnamed registration we additionally expose an
    ///     unkeyed <see cref="IGeodeCache"/> alias for the
    ///     single-cluster injection path.
    ///   </item>
    /// </list>
    /// <para>
    /// Logging is intentionally not registered here; callers are
    /// expected to add their own <c>ILoggerFactory</c> via
    /// <c>AddLogging()</c> / Serilog / etc.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddCore(IServiceCollection services, string? name)
    {
        var key = name ?? MsOptions.DefaultName;

        services.TryAddScoped<CacheScopeContext>();
        services.TryAddScoped<ClientProxyMembershipIdBuilder>();
        services.TryAddScoped<PoolManager>();
        services.TryAddScoped<TcrConnectionManager>();
        // Cache itself is Scoped — one instance per per-cache
        // AsyncServiceScope (created by GeodeCacheFactory). Lets the
        // factory resolve via GetRequiredService<Cache>() rather than
        // ActivatorUtilities, and lets scope.DisposeAsync() cascade the
        // Cache's IAsyncDisposable automatically.
        services.TryAddScoped<Services.Cache>();
        services.TryAddSingleton<TcrPartBuilder>();
        // SerializationRegistry is per-cache (Scoped) so multi-cluster
        // setups can register different PDX types per cluster without
        // leaking — see cppcache CacheImpl::m_serializationRegistry.
        // TcrMessageBuilder must drop from Singleton to Scoped because
        // it now depends on the Scoped registry (Singleton → Scoped
        // would be a captive-dependency lifetime violation).
        services.TryAddScoped<SerializationRegistry>();
        // TypedResultAdapter shares SerializationRegistry's per-cache
        // scope. Stateless today, but Scoped now leaves room for
        // future per-cache reflection caches / PDX type rules without
        // re-litigating the lifetime when those land.
        services.TryAddScoped<TypedResultAdapter>();
        services.TryAddScoped<TcrMessageBuilder>();
        services.TryAddScoped<TcrMessageHelper>();
        // EventIdGenerator is per-cache (Scoped) — mirrors cppcache
        // EventIdTSS, which sits inside CacheImpl. Each cache instance
        // gets its own monotonic seq, so closing and rebuilding a cache
        // resets the counter (clientId rotates anyway, so server-side
        // dedup keys don't collide).
        services.TryAddScoped<EventIdGenerator>();

        // IValidateOptions<T> is an additive abstraction: the options
        // pipeline runs every registered validator. TryAddEnumerable
        // ensures we only contribute one instance even when the user
        // calls AddGeodeClient multiple times (multi-cluster scenario),
        // while still leaving room for user-supplied validators to
        // coexist.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GeodeClientOptions>, GeodeClientOptionsValidator>());
        // TcrConnection is intentionally NOT registered: it's a
        // stateful resource (owns a Socket / Stream / handshake state),
        // not a stateless service. Production path opens one through
        // TcrEndpoint.CreateNewConnectionAsync via
        // ActivatorUtilities.CreateInstance<TcrConnection>(sp); tests
        // do the same. Registering it would invite misuse via
        // GetRequiredService<TcrConnection>() — which hands back a
        // disconnected instance that still needs ConnectAsync.
        services.TryAddSingleton<IGeodeCacheFactory, GeodeCacheFactory>();

        services.AddKeyedSingleton<IGeodeCache>(
            key,
            static (sp, k) => sp.GetRequiredService<IGeodeCacheFactory>().Get((string)k!));

        if (name is null)
        {
            services.TryAddSingleton<IGeodeCache>(
                static sp => sp.GetRequiredService<IGeodeCacheFactory>().Get());
        }

        return services;
    }
}
