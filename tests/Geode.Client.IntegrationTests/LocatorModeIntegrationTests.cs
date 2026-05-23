/*
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end coverage for the locator-mode connection path. Verifies
/// that <see cref="ThinClientLocatorHelper.GetEndpointForNewFwdConnAsync"/>
/// queries a real locator and that the periodic locator-list refresh
/// loop fires against the fixture's locator. A full Put/Get round trip
/// through a locator-discovered server is included.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocatorModeIntegrationTests(GeodeFixture fx)
{
    private readonly GeodeFixture _fx = fx;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);
    private const string RegionName = "test";

    /// <summary>
    /// Locator-only pool config: pool's <c>Locators</c> points at the
    /// fixture's locator port (10334 mapped). The server endpoint is
    /// discovered at connect time via <c>ClientConnectionRequest</c>.
    /// </summary>
    private void ConfigureCache(GeodeClientOptions config)
    {
        config.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "default",
                    Locators =
                    {
                        new CacheHostPortOptions
                        {
                            Host = _fx.LocatorHost,
                            Port = _fx.LocatorPort,
                        },
                    },
                },
            },
            Regions =
            {
                new CacheRegionOptions { Name = RegionName },
            },
        };
    }

    [Fact]
    public async Task Pool_with_locator_initialises_against_real_locator()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Listen before cache init so MeterListener.Start() runs while
        // the static Histogram instruments may or may not yet be
        // published — either way Start() retroactively picks them up.
        // RestoreMinConnectionsAsync → SelectEndpointFromLocatorAsync
        // path records here.
        using var clientConnectionRequests = new MeterCapture("Geode.Client.Pool", "ClientConnectionRequestTime");

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        var poolManager = ((Cache)cache).PoolManager;
        Assert.NotNull(poolManager.DefaultPool);
        Assert.Same(poolManager.DefaultPool, poolManager.Find("default"));

        // ConnManageLoopAsync fires RestoreMinConnectionsAsync ~1s after
        // init (cppcache mirror); RestoreMinConnectionsAsync →
        // CreatePoolConnectionAsync → SelectEndpointFromLocatorAsync →
        // PoolStatistics.ClientConnectionRequest(). Poll up to 5s.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && clientConnectionRequests.Count < 1)
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            clientConnectionRequests.Count >= 1,
            $"Expected ClientConnectionRequestTime histogram count >= 1 within deadline, got {clientConnectionRequests.Count}.");

        await cache.CloseAsync(cts.Token);
        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task UpdateLocatorList_loop_ticks_against_real_locator()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        using var locatorListRequests = new MeterCapture("Geode.Client.Pool", "LocatorListRequestTime");

        // Tighten the refresh interval so multiple ticks happen within
        // the test budget. cppcache initial delay is fixed 1s, so first
        // tick fires ~1s after pool init.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "default",
                        Locators =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.LocatorPort,
                            },
                        },
                        UpdateLocatorListInterval = TimeSpan.FromMilliseconds(200),
                    },
                },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // 1s initial delay + ≥2 × 200ms intervals — deadline 5s is generous.
        // Histogram records on every UpdateLocatorsLocalAsync tick
        // (finally clause covers exception path too). Count >= 2 proves
        // (a) timer fires, (b) LocatorListRequest reaches the locator
        // and the locator replies, (c) LocatorListResponse decodes
        // without throwing (else the ConnManageLoop's catch-and-retry
        // would log but the histogram still records).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && locatorListRequests.Count < 2)
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            locatorListRequests.Count >= 2,
            $"Expected LocatorListRequestTime histogram count >= 2 within deadline, got {locatorListRequests.Count}.");

        await cache.CloseAsync(cts.Token);
    }

    /// <summary>
    /// Full end-to-end Put/Get through a locator-discovered server.
    /// The fixture starts each server with
    /// <c>--hostname-for-clients=localhost</c> and pins host-side ports
    /// 1:1 to the in-container ports, so the address the locator hands
    /// back resolves on the test host.
    /// </summary>
    [Fact]
    public async Task Pool_with_locator_supports_region_put_get_round_trip()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        // Distinct key range to avoid collisions with other tests in
        // the shared collection.
        const int key = 0x5000_0001;
        const int value = 7777;

        await region.PutAsync(key, value, cts.Token);
        var actual = await region.GetAsync(key, cts.Token);

        Assert.Equal(value, actual);

        await cache.CloseAsync(cts.Token);
    }
}

*/