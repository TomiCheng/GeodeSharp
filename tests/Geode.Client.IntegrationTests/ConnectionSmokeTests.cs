using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Walking-skeleton end-to-end: build a cache + pool against a real Geode
/// server and verify the client at least reaches a "connected" state via
/// the background ConnManageLoop.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ConnectionSmokeTests(GeodeFixture fx)
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sanity_FixtureInjected()
    {
        // If the fixture didn't initialize, ServerPort defaults to 40404 but
        // the container DID actually map ports — gfsh `version` proves the
        // container is reachable.
        Assert.NotNull(fx);
        // After init, the host should be assigned (not the class default).
        // Container hostname is something like "localhost" or a docker IP.
        Assert.NotEmpty(fx.LocatorHost);
    }

    [Fact]
    public async Task FixtureContainer_IsActuallyRunning()
    {
        // Force-touch the fixture by calling gfsh inside the container.
        // If the container isn't running, this throws.
        var output = await fx.GfshAsync("list members", TestContext.Current.CancellationToken);
        Assert.Contains("loc1", output);
    }

    [Fact]
    public async Task BuildAsync_AgainstRealServer_OpensConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        using var capture = new MeterCapture("Geode.Client.Pool", "PoolConnections");

        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .BuildAsync("p", ct);

        // ConnManageLoop opens conns fire-and-forget; poll up to 5s for the
        // background RestoreMinConnections tick to land a connected endpoint.
        // PoolConnections is an ObservableGauge — Observe() pulls the value.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            capture.Observe();
            if (capture.LastValue > 0) break;
            await Task.Delay(100, ct);
        }

        Assert.True(capture.LastValue > 0,
            $"Expected PoolConnections > 0 after 5s, got {capture.LastValue}.");
    }
}
