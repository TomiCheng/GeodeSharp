using Geode.Client.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client.Tests;

/// <summary>
/// Unit tests for <see cref="GeodeClientExtensions"/>'s 3 overload × 2
/// (named / unnamed) registration matrix and the resolution paths that
/// flow from each.
/// </summary>
public class GeodeClientExtensionsTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv).Build();

    private static GeodeClientOptions Bound(IServiceProvider sp, string name) =>
        sp.GetRequiredService<IOptionsMonitor<GeodeClientOptions>>().Get(name);

    // ---- unnamed registrations ----------------------------------------

    [Fact]
    public async Task Unnamed_BindConfiguration_DefaultSection()
    {
        var cfg = BuildConfig(new Dictionary<string, string?> { ["Geode:Name"] = "single" });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddGeodeClient();
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("single", Bound(sp, MsOptions.DefaultName).Name);
        Assert.Equal(string.Empty, sp.GetRequiredService<IGeodeCache>().Name);
    }

    [Fact]
    public async Task Unnamed_BindFromConfigurationArg()
    {
        var cfg = BuildConfig(new Dictionary<string, string?> { ["Name"] = "from-arg" });
        var services = new ServiceCollection();
        services.AddGeodeClient(cfg);
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("from-arg", Bound(sp, MsOptions.DefaultName).Name);
        Assert.NotNull(sp.GetRequiredService<IGeodeCache>());
    }

    [Fact]
    public async Task Unnamed_ProgrammaticConfigure()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(opt => opt.Name = "code-set");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("code-set", Bound(sp, MsOptions.DefaultName).Name);
    }

    // ---- named registrations ------------------------------------------

    [Fact]
    public async Task Named_BindConfiguration_NameAsSection()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["geode1:Name"] = "n1",
            ["geode2:Name"] = "n2",
        });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
        services.AddGeodeClient("geode1");
        services.AddGeodeClient("geode2");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("n1", Bound(sp, "geode1").Name);
        Assert.Equal("n2", Bound(sp, "geode2").Name);

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        Assert.Equal("geode1", f.Get("geode1").Name);
        Assert.Equal("geode2", f.Get("geode2").Name);
    }

    [Fact]
    public async Task Named_BindFromConfigurationArg()
    {
        var cfg = BuildConfig(new Dictionary<string, string?> { ["Name"] = "named-arg" });
        var services = new ServiceCollection();
        services.AddGeodeClient(cfg, "primary");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("named-arg", Bound(sp, "primary").Name);
    }

    [Fact]
    public async Task Named_ProgrammaticConfigure()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(opt => opt.Name = "g1-code", "g1");
        await using var sp = services.BuildServiceProvider();

        Assert.Equal("g1-code", Bound(sp, "g1").Name);
        Assert.Equal("g1", sp.GetRequiredService<IGeodeCacheFactory>().Get("g1").Name);
    }

    // ---- factory & keyed-DI behaviour ---------------------------------

    [Fact]
    public async Task Factory_Get_ReturnsSameInstanceAcrossCalls()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(_ => { });
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        Assert.Same(f.Get(), f.Get());
    }

    [Fact]
    public async Task Factory_DifferentNames_ReturnDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(_ => { }, "g1");
        services.AddGeodeClient(_ => { }, "g2");
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        Assert.NotSame(f.Get("g1"), f.Get("g2"));
    }

    [Fact]
    public async Task KeyedService_AndFactory_ReturnSameInstance()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(_ => { }, "g1");
        await using var sp = services.BuildServiceProvider();

        var fromFactory = sp.GetRequiredService<IGeodeCacheFactory>().Get("g1");
        var fromKeyed = sp.GetRequiredKeyedService<IGeodeCache>("g1");
        Assert.Same(fromFactory, fromKeyed);
    }

    [Fact]
    public async Task Factory_Get_NullName_Throws()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(_ => { });
        await using var sp = services.BuildServiceProvider();

        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        Assert.Throws<ArgumentNullException>(() => f.Get(null!));
    }

    // ---- mixed / negative ---------------------------------------------

    [Fact]
    public async Task Mixed_UnnamedAndNamed_Coexist()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(opt => opt.Name = "default-cluster");
        services.AddGeodeClient(opt => opt.Name = "legacy-cluster", "legacy");
        await using var sp = services.BuildServiceProvider();

        // unnamed via plain injection
        var def = sp.GetRequiredService<IGeodeCache>();
        Assert.Equal(string.Empty, def.Name);
        Assert.Equal("default-cluster", Bound(sp, MsOptions.DefaultName).Name);

        // named via factory + keyed DI yield the same object
        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        var fromKeyed = sp.GetRequiredKeyedService<IGeodeCache>("legacy");
        Assert.Same(f.Get("legacy"), fromKeyed);
        Assert.Equal("legacy", f.Get("legacy").Name);
        Assert.Equal("legacy-cluster", Bound(sp, "legacy").Name);
    }

    [Fact]
    public async Task NamedOnly_PlainInjection_Throws()
    {
        var services = new ServiceCollection();
        services.AddGeodeClient(_ => { }, "only-named");
        await using var sp = services.BuildServiceProvider();

        // no unnamed registration -> the unkeyed alias is absent.
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IGeodeCache>());
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
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddGeodeClient((IConfiguration)null!));
    }

    [Fact]
    public void AddGeodeClient_NullConfigure_Throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddGeodeClient((Action<GeodeClientOptions>)null!));
    }
}
