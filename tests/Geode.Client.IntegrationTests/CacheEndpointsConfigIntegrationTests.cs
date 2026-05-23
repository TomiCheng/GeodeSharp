/*
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end coverage for the <see cref="CacheOptions.Endpoints"/>
/// path — the modernised analogue of cppcache
/// <c>&lt;client-cache endpoints=&gt;</c>. Verifies that the synthesis
/// in <see cref="Cache.ResolvePoolsToBuild"/> wires up correctly at
/// runtime: a default-named pool is registered, becomes the
/// <see cref="PoolManager.DefaultPool"/>, and supports a real Put/Get
/// round trip against the fixture container.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CacheEndpointsConfigIntegrationTests(GeodeFixture fx)
{
    private readonly GeodeFixture _fx = fx;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);
    private const string RegionName = "test";

    /// <summary>
    /// Endpoints-only configuration: no <see cref="CacheOptions.Pools"/>
    /// declared, server addresses live in
    /// <see cref="CacheOptions.Endpoints"/>. The region's empty
    /// <c>PoolName</c> falls through to
    /// <see cref="PoolManager.DefaultPool"/>, which is the synthesised
    /// pool.
    /// </summary>
    private void ConfigureCache(GeodeClientOptions config)
    {
        config.Cache = new CacheOptions
        {
            Endpoints =
            {
                new CacheHostPortOptions
                {
                    Host = _fx.LocatorHost,
                    Port = _fx.ServerPort,
                },
            },
            Regions =
            {
                new CacheRegionOptions
                {
                    Name = RegionName,
                    // No PoolName → resolves to DefaultPool.
                },
            },
        };
    }

    [Fact]
    public async Task Endpoints_only_synthesises_default_pool_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // Synthesis: Endpoints → one CachePoolOptions { Name="default" }.
        // Registered first → becomes DefaultPool (PoolManager.AddPool
        // semantics: first-wins CompareExchange).
        var poolManager = ((Cache)cache).PoolManager;
        Assert.NotNull(poolManager.DefaultPool);
        Assert.Same(poolManager.DefaultPool, poolManager.Find("default"));
        Assert.Single(poolManager.GetAll());

        await cache.CloseAsync(cts.Token);
        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task Endpoints_only_supports_region_put_get_round_trip()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // See FreshConnectionSettleDelay (mirrors RegionCrudIntegrationTests).
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        // Key range distinct from RegionCrudIntegrationTests to avoid
        // collision when xUnit runs collections in parallel against a
        // shared fixture container.
        const int key = 0x6000_0001;
        const int value = 4242;

        await region.PutAsync(key, value, cts.Token);
        var actual = await region.GetAsync(key, cts.Token);

        Assert.Equal(value, actual);

        await cache.CloseAsync(cts.Token);
    }
}

*/