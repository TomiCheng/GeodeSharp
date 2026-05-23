using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geode.Client.Tests;

public class PoolFactoryTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static PoolFactory BuildFactory(ServiceProvider sp)
    {
        var cache = sp.GetRequiredService<IGeodeCacheFactory>().Create("c");
        return cache.PoolManager.CreateFactory();
    }

    [Fact]
    public async Task Setters_ReturnSameFactoryInstance()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp);

        Assert.Same(f, f.SetMinConnections(1));
        Assert.Same(f, f.SetMaxConnections(10));
        Assert.Same(f, f.SetIdleTimeout(TimeSpan.FromSeconds(5)));
        Assert.Same(f, f.AddServer("h", 40404));
        Assert.Same(f, f.Reset());
    }

    [Fact]
    public async Task AddLocator_AfterAddServer_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp);
        f.AddServer("h", 40404);

        Assert.Throws<ArgumentException>(() => f.AddLocator("h", 10334));
    }

    [Fact]
    public async Task AddServer_AfterAddLocator_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp);
        f.AddLocator("h", 10334);

        Assert.Throws<ArgumentException>(() => f.AddServer("h", 40404));
    }

    [Fact]
    public async Task BuildAsync_WithoutEndpoints_ThrowsOptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp);

        await Assert.ThrowsAsync<OptionsValidationException>(() => f.BuildAsync("p", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_MaxConnectionsLessThanMin_ThrowsOptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp)
            .AddServer("h", 40404)
            .SetMinConnections(10)
            .SetMaxConnections(5);

        await Assert.ThrowsAsync<OptionsValidationException>(() => f.BuildAsync("p", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_NegativeIdleTimeout_ThrowsOptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp)
            .AddServer("h", 40404)
            .SetIdleTimeout(TimeSpan.FromSeconds(-1));

        await Assert.ThrowsAsync<OptionsValidationException>(() => f.BuildAsync("p", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_InvalidPort_ThrowsOptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp).AddServer("h", 0);

        await Assert.ThrowsAsync<OptionsValidationException>(() => f.BuildAsync("p", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_EmptyHost_ThrowsOptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp).AddServer("", 40404);

        await Assert.ThrowsAsync<OptionsValidationException>(() => f.BuildAsync("p", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reset_ClearsEndpoints()
    {
        await using var sp = BuildSp();
        var f = BuildFactory(sp);
        f.AddLocator("h", 10334);

        // Sanity check: mutual exclusion is active before Reset.
        Assert.Throws<ArgumentException>(() => f.AddServer("h", 40404));

        f.Reset();

        // After Reset, switching endpoint kind succeeds.
        f.AddServer("h", 40404);
    }
}
