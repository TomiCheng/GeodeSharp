using Geode.Client.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client.Tests;

/// <summary>
/// Unit tests for <see cref="GeodeClientExtensions"/>'s six-overload
/// surface (3 × <c>AddGeodeClient</c> + 3 × <c>AddGeodeFactory</c>)
/// and the resolution paths that flow from each. Manual
/// <see cref="IGeodeCacheFactory.Create"/> is required before any
/// <see cref="IGeodeCache"/> retrieval — the tests follow that contract.
/// </summary>
public class GeodeClientExtensionsTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv).Build();

    /// <summary>
    /// Minimum pool config that satisfies <c>GeodeClientOptionsValidator</c>:
    /// one pool named "test" with one server entry. Tests that exercise
    /// DI shape only and don't care about pool contents use this as the
    /// configure delegate.
    /// </summary>
    private static void MinimalPool(GeodeClientOptions opt) =>
        opt.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "test",
                    Servers = { new CacheHostPortOptions { Host = "localhost", Port = 40404 } },
                },
            },
        };

    /// <summary>
    /// IConfiguration-shaped equivalent of <see cref="MinimalPool"/>.
    /// </summary>
    private static void AddMinimalPoolKeys(IDictionary<string, string?> kv, string sectionPrefix = "")
    {
        kv[$"{sectionPrefix}Cache:Pools:0:Name"] = "test";
        kv[$"{sectionPrefix}Cache:Pools:0:Servers:0:Host"] = "localhost";
        kv[$"{sectionPrefix}Cache:Pools:0:Servers:0:Port"] = "40404";
    }

    private static GeodeClientOptions Bound(IServiceProvider sp, string name) =>
        sp.GetRequiredService<IOptionsMonitor<GeodeClientOptions>>().Get(name);

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    // ---- AddGeodeClient (unnamed) -------------------------------------

    [Fact]
    public async Task AddGeodeClient_BindConfiguration_DefaultSection()
    {
        var cfgKeys = new Dictionary<string, string?> { ["Geode:Name"] = "single" };
        AddMinimalPoolKeys(cfgKeys, "Geode:");
        var cfg = BuildConfig(cfgKeys);
        var services = NewServices();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddGeodeClient();
        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IGeodeCacheFactory>().Create();

        Assert.Equal("single", Bound(sp, MsOptions.DefaultName).Name);
        Assert.Equal(string.Empty, sp.GetRequiredService<IGeodeCache>().Name);
    }

    [Fact]
    public async Task AddGeodeClient_BindFromConfigurationArg()
    {
        var cfgKeys = new Dictionary<string, string?> { ["Name"] = "from-arg" };
        AddMinimalPoolKeys(cfgKeys);
        var cfg = BuildConfig(cfgKeys);
        var services = NewServices();
        services.AddGeodeClient(cfg);
        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IGeodeCacheFactory>().Create();

        Assert.Equal("from-arg", Bound(sp, MsOptions.DefaultName).Name);
        Assert.NotNull(sp.GetRequiredService<IGeodeCache>());
    }

    [Fact]
    public async Task AddGeodeClient_ProgrammaticConfigure()
    {
        var services = NewServices();
        services.AddGeodeClient(opt => { MinimalPool(opt); opt.Name = "code-set"; });
        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IGeodeCacheFactory>().Create();

        Assert.Equal("code-set", Bound(sp, MsOptions.DefaultName).Name);
    }

    // ---- AddGeodeFactory (named) --------------------------------------

    [Fact]
    public async Task AddGeodeFactory_BindConfiguration_NameAsSection()
    {
        var cfgKeys = new Dictionary<string, string?>
        {
            ["geode1:Name"] = "n1",
            ["geode2:Name"] = "n2",
        };
        AddMinimalPoolKeys(cfgKeys, "geode1:");
        AddMinimalPoolKeys(cfgKeys, "geode2:");
        var cfg = BuildConfig(cfgKeys);
        var services = NewServices();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddGeodeFactory("geode1");
        services.AddGeodeFactory("geode2");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("n1", Bound(sp, "geode1").Name);
        Assert.Equal("n2", Bound(sp, "geode2").Name);

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        f.Create("geode1", "geode1");
        f.Create("geode2", "geode2");

        Assert.Equal("geode1", f.Get("geode1").Name);
        Assert.Equal("geode2", f.Get("geode2").Name);
    }

    [Fact]
    public async Task AddGeodeFactory_BindFromConfigurationArg()
    {
        var cfgKeys = new Dictionary<string, string?> { ["Name"] = "named-arg" };
        AddMinimalPoolKeys(cfgKeys);
        var cfg = BuildConfig(cfgKeys);
        var services = NewServices();
        services.AddGeodeFactory(cfg, "primary");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("named-arg", Bound(sp, "primary").Name);
    }

    [Fact]
    public async Task AddGeodeFactory_ProgrammaticConfigure()
    {
        var services = NewServices();
        services.AddGeodeFactory(opt => { MinimalPool(opt); opt.Name = "g1-code"; }, "g1");
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        f.Create("g1", "g1");

        Assert.Equal("g1-code", Bound(sp, "g1").Name);
        Assert.Equal("g1", f.Get("g1").Name);
    }

    // ---- factory behaviour --------------------------------------------

    [Fact]
    public async Task Factory_Get_ReturnsSameInstanceAcrossCalls()
    {
        var services = NewServices();
        services.AddGeodeClient(MinimalPool);
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        var built = f.Create();
        Assert.Same(built, f.Get());
        Assert.Same(f.Get(), f.Get());
    }

    [Fact]
    public async Task Factory_DifferentNames_ReturnDifferentInstances()
    {
        var services = NewServices();
        services.AddGeodeFactory(MinimalPool, "g1");
        services.AddGeodeFactory(MinimalPool, "g2");
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        f.Create("g1", "g1");
        f.Create("g2", "g2");

        Assert.NotSame(f.Get("g1"), f.Get("g2"));
    }

    [Fact]
    public async Task Factory_Get_NullName_Throws()
    {
        var services = NewServices();
        services.AddGeodeClient(MinimalPool);
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        Assert.Throws<ArgumentNullException>(() => f.Get(null!));
    }

    // ---- mixed / negative ---------------------------------------------

    [Fact]
    public async Task Mixed_AddGeodeClient_And_AddGeodeFactory_Coexist()
    {
        var services = NewServices();
        services.AddGeodeClient(opt => { MinimalPool(opt); opt.Name = "default-cluster"; });
        services.AddGeodeFactory(opt => { MinimalPool(opt); opt.Name = "legacy-cluster"; }, "legacy");
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        f.Create();                         // unnamed default
        f.Create("legacy", "legacy");       // named

        // unnamed via plain injection
        var def = sp.GetRequiredService<IGeodeCache>();
        Assert.Equal(string.Empty, def.Name);
        Assert.Equal("default-cluster", Bound(sp, MsOptions.DefaultName).Name);

        // named via factory only — AddGeodeFactory does not register a
        // DI alias for IGeodeCache.
        Assert.Equal("legacy", f.Get("legacy").Name);
        Assert.Equal("legacy-cluster", Bound(sp, "legacy").Name);
    }

    [Fact]
    public async Task AddGeodeFactory_Only_IGeodeCache_Injection_Throws()
    {
        var services = NewServices();
        services.AddGeodeFactory(MinimalPool, "only-named");
        await using var sp = services.BuildServiceProvider();

        // AddGeodeFactory does NOT register the unkeyed IGeodeCache
        // alias — direct injection has no descriptor to resolve.
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IGeodeCache>());
    }

    // ---- factory disposal ---------------------------------------------

    [Fact]
    public async Task FactoryDispose_CascadesTo_AllCachedCaches()
    {
        var services = NewServices();
        services.AddGeodeFactory(MinimalPool, "g1");
        services.AddGeodeFactory(MinimalPool, "g2");
        var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        var c1 = f.Create("g1", "g1");
        var c2 = f.Create("g2", "g2");
        Assert.False(c1.IsClosed);
        Assert.False(c2.IsClosed);

        await sp.DisposeAsync();

        Assert.True(c1.IsClosed);
        Assert.True(c2.IsClosed);
    }

    [Fact]
    public async Task FactoryDispose_IsIdempotent()
    {
        var services = NewServices();
        services.AddGeodeClient(MinimalPool);
        await using var sp = services.BuildServiceProvider();

        var disposable = (IAsyncDisposable)sp.GetRequiredService<IGeodeCacheFactory>();
        await disposable.DisposeAsync();
        await disposable.DisposeAsync();   // second call must be a no-op
    }

    [Fact]
    public async Task Factory_Get_AfterDispose_Throws()
    {
        var services = NewServices();
        services.AddGeodeClient(MinimalPool);
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        await ((IAsyncDisposable)f).DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => f.Get());
        Assert.Throws<ObjectDisposedException>(() => f.Get("any"));
    }

    // ---- guard clauses on the public API ------------------------------

    [Fact]
    public void AddGeodeClient_NullServices_Throws()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddGeodeClient());
    }

    [Fact]
    public void AddGeodeClient_NullConfiguration_Throws()
    {
        var services = NewServices();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddGeodeClient((IConfiguration)null!));
    }

    [Fact]
    public void AddGeodeClient_NullConfigure_Throws()
    {
        var services = NewServices();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddGeodeClient((Action<GeodeClientOptions>)null!));
    }

    [Fact]
    public void AddGeodeFactory_NullServices_Throws()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddGeodeFactory("any"));
    }

    [Fact]
    public void AddGeodeFactory_NullName_Throws()
    {
        var services = NewServices();
        Assert.Throws<ArgumentNullException>(() => services.AddGeodeFactory(null!));
    }
}
