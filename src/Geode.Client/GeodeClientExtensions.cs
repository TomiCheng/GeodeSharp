//using Geode.Client.Internal;
//using Geode.Client.Options;
//using Geode.Client.Pdx;
//using Geode.Client.Protocol;
//using Geode.Client.Protocol.Serialization;
//using Microsoft.Extensions.Configuration;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Geode.Client;

/// <summary>
/// DI registration entry points for the Geode managed client.
/// </summary>
public static class GeodeClientExtensions
{
    ///// <summary>
    ///// Default <see cref="IConfiguration"/> section name used by
    ///// overloads that bind from the host <see cref="IConfiguration"/>:
    ///// <see cref="AddGeodeClient(IServiceCollection)"/> and
    ///// <see cref="AddGeodeFactory(IServiceCollection, string)"/> with
    ///// empty <c>name</c>.
    ///// </summary>
    //public const string DefaultSectionName = "Geode";

    //// ── AddGeodeClient ─ register the unnamed default + IGeodeCache alias ──

    ///// <summary>Register the default config (bound from <see cref="DefaultSectionName"/>) and the <see cref="IGeodeCache"/> injection alias.</summary>
    //public static IServiceCollection AddGeodeClient(this IServiceCollection services)
    //{
    //    ArgumentNullException.ThrowIfNull(services);

    //    services.AddOptions<GeodeClientOptions>("")
    //        .BindConfiguration(DefaultSectionName)
    //        .ValidateOnStart();
    //    AddCore(services);
    //    RegisterUnnamedCacheAlias(services);
    //    return services;
    //}

    ///// <summary>Register the default config (bound from <paramref name="configuration"/>) and the <see cref="IGeodeCache"/> injection alias.</summary>
    //public static IServiceCollection AddGeodeClient(
    //    this IServiceCollection services,
    //    IConfiguration configuration)
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentNullException.ThrowIfNull(configuration);

    //    services.AddOptions<GeodeClientOptions>("")
    //        .Bind(configuration)
    //        .ValidateOnStart();
    //    AddCore(services);
    //    RegisterUnnamedCacheAlias(services);
    //    return services;
    //}

    ///// <summary>Register the default config (programmatic) and the <see cref="IGeodeCache"/> injection alias.</summary>
    //public static IServiceCollection AddGeodeClient(
    //    this IServiceCollection services,
    //    Action<GeodeClientOptions> configure)
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentNullException.ThrowIfNull(configure);

    //    services.AddOptions<GeodeClientOptions>("")
    //        .Configure(configure)
    //        .ValidateOnStart();
    //    AddCore(services);
    //    RegisterUnnamedCacheAlias(services);
    //    return services;
    //}

    //// ── AddGeodeFactory ─ register a named config; no IGeodeCache alias ──

    ///// <summary>
    ///// Register a named config bound from the host <see cref="IConfiguration"/>.
    ///// Section name is <paramref name="name"/> when non-empty,
    ///// otherwise <see cref="DefaultSectionName"/>. Retrieve the cache
    ///// via <see cref="IGeodeCacheFactory"/>.
    ///// </summary>
    //public static IServiceCollection AddGeodeFactory(
    //    this IServiceCollection services,
    //    string name = "")
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentNullException.ThrowIfNull(name);

    //    var section = string.IsNullOrEmpty(name) ? DefaultSectionName : name;
    //    services.AddOptions<GeodeClientOptions>(name)
    //        .BindConfiguration(section)
    //        .ValidateOnStart();
    //    return AddCore(services);
    //}

    ///// <summary>Register a named config bound from <paramref name="configuration"/>. Retrieve via <see cref="IGeodeCacheFactory"/>.</summary>
    //public static IServiceCollection AddGeodeFactory(
    //    this IServiceCollection services,
    //    IConfiguration configuration,
    //    string name = "")
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentNullException.ThrowIfNull(configuration);
    //    ArgumentNullException.ThrowIfNull(name);

    //    services.AddOptions<GeodeClientOptions>(name)
    //        .Bind(configuration)
    //        .ValidateOnStart();
    //    return AddCore(services);
    //}

    ///// <summary>Register a named config (programmatic). Retrieve via <see cref="IGeodeCacheFactory"/>.</summary>
    //public static IServiceCollection AddGeodeFactory(
    //    this IServiceCollection services,
    //    Action<GeodeClientOptions> configure,
    //    string name = "")
    //{
    //    ArgumentNullException.ThrowIfNull(services);
    //    ArgumentNullException.ThrowIfNull(configure);
    //    ArgumentNullException.ThrowIfNull(name);

    //    services.AddOptions<GeodeClientOptions>(name)
    //        .Configure(configure)
    //        .ValidateOnStart();
    //    return AddCore(services);
    //}

    //// ── private helpers ───────────────────────────────────────────

    ///// <summary>
    ///// Shared registration body — singleton factory plus the per-cache
    ///// Scoped services that every <see cref="Services.Cache"/> instance
    ///// requires. Idempotent via <c>TryAdd*</c>: multiple
    ///// <see cref="AddGeodeClient(IServiceCollection)"/> /
    ///// <see cref="AddGeodeFactory(IServiceCollection, string)"/> calls
    ///// (with different names) share one factory and one set of service
    ///// descriptors.
    ///// </summary>
    //private static IServiceCollection AddCore(IServiceCollection services)
    //{
    //    services.TryAddScoped<CacheScopeContext>();
    //    services.TryAddScoped<ClientProxyMembershipIdBuilder>();
    //    services.TryAddScoped<PoolManager>();
    //    services.TryAddScoped<TcrConnectionManager>();
    //    services.TryAddScoped<Services.Cache>();
    //    services.TryAddSingleton<TcrPartBuilder>();
    //    services.TryAddScoped<SerializationRegistry>();
    //    services.TryAddScoped<PdxTypeRegistry>();
    //    services.TryAddScoped<TypeRegistry>();
    //    services.TryAddScoped<TypedResultAdapter>();
    //    services.TryAddScoped<TcrMessageBuilder>();
    //    services.TryAddScoped<TcrMessageHelper>();
    //    services.TryAddScoped<EventIdGenerator>();
    //    services.TryAddScoped<MemberListForVersionStamp>();

    //    services.TryAddEnumerable(
    //        ServiceDescriptor.Singleton<IValidateOptions<GeodeClientOptions>, GeodeClientOptionsValidator>());

    //    services.TryAddSingleton<IGeodeCacheFactory, GeodeCacheFactory>();

    //    return services;
    //}

    ///// <summary>
    ///// Register the unnamed-default <see cref="IGeodeCache"/> alias.
    ///// Resolution goes through <see cref="IGeodeCacheFactory.Get"/> —
    ///// throws <see cref="KeyNotFoundException"/> if the consumer
    ///// forgot to call <see cref="IGeodeCacheFactory.Create"/> at host
    ///// startup.
    ///// </summary>
    //private static void RegisterUnnamedCacheAlias(IServiceCollection services)
    //{
    //    services.TryAddSingleton<IGeodeCache>(static sp =>
    //        sp.GetRequiredService<IGeodeCacheFactory>().Get(""));
    //}

    public static IServiceCollection AddGeodeFactory(this IServiceCollection services)
    {
        services.TryAddSingleton<IGeodeCacheFactory, GeodeCacheFactory>();
        services.TryAddScoped<SystemProperties>();
        services.TryAddScoped<GeodeCache>();
        services.TryAddScoped<EventIdGenerator>();
        services.TryAddScoped<TcrConnectionManager>();
        services.TryAddScoped<PoolManager>();
        services.TryAddScoped<TypedResultAdapter>();
        services.TryAddScoped<TypeRegistry>();
        services.TryAddScoped<PdxTypeRegistry>();
        services.TryAddScoped<SerializationRegistry>();
        services.TryAddSingleton<DmContextAccessor>();
        return services;
    }
}
