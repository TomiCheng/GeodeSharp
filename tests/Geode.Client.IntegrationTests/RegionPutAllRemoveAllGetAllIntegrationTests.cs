using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end checks for the three bulk-op variants:
/// <see cref="IRegion.PutAllAsync(IReadOnlyDictionary{object, object}, CancellationToken)"/>,
/// <see cref="IRegion.RemoveAllAsync(IReadOnlyCollection{object}, CancellationToken)"/>,
/// <see cref="IRegion.GetAllAsync(IReadOnlyCollection{object}, CancellationToken)"/>.
/// Wire layout is unit-locked; integration exercises the
/// chunked-reply pipeline against real Apache Geode.
/// Key range <c>6000s</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionPutAllRemoveAllGetAllIntegrationTests(GeodeFixture fx)
{
    private const string RegionName = "test";
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private async Task<IRegion<int, string>> BuildRegionAsync(ServiceProvider sp, CancellationToken ct)
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .BuildAsync("p", ct);
        await Task.Delay(FreshConnectionSettleDelay, ct);
        return await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<int, string>(RegionName, ct);
    }

    // ── PutAll ────────────────────────────────────────────────────

    [Fact]
    public async Task PutAllAsync_ThreeEntries_AllVisibleOnServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var map = new Dictionary<object, object>
        {
            [6001] = "a",
            [6002] = "b",
            [6003] = "c",
        };

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        try
        {
            await region.PutAllAsync(map, ct);

            // Cross-check each key via client Get (round-trip through wire layer).
            Assert.Equal("a", await region.GetAsync(6001, ct));
            Assert.Equal("b", await region.GetAsync(6002, ct));
            Assert.Equal("c", await region.GetAsync(6003, ct));
        }
        finally
        {
            foreach (var key in map.Keys)
            {
                await fx.GfshAsync(
                    $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                    ct);
            }
        }
    }

    // ── RemoveAll ─────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAllAsync_ThreePreputKeys_AllGone()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = new object[] { 6011, 6012, 6013 };

        foreach (var key in keys)
        {
            await fx.GfshAsync(
                $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value=v --value-class=java.lang.String",
                ct);
        }

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        await region.RemoveAllAsync(keys, ct);

        foreach (var key in keys)
        {
            Assert.Null(await region.GetAsync((int)key, ct));
        }
    }

    // ── GetAll ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_GfshPrePut_ReturnsAllValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new Dictionary<int, string>
        {
            [6021] = "alpha",
            [6022] = "beta",
            [6023] = "gamma",
        };

        foreach (var (key, value) in expected)
        {
            await fx.GfshAsync(
                $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value={value} --value-class=java.lang.String",
                ct);
        }

        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            var result = await region.GetAllAsync(expected.Keys.Cast<object>().ToArray(), ct);

            Assert.Equal(expected.Count, result.Count);
            foreach (var (key, value) in expected)
            {
                Assert.Equal(value, result[key]);
            }
        }
        finally
        {
            foreach (var key in expected.Keys)
            {
                await fx.GfshAsync(
                    $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                    ct);
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_MixedPresentAbsent_NullsForAbsent()
    {
        // Pre-put one of three keys; GetAll returns null for the two
        // absent ones (cppcache m_byteArray[i]==3 stores null).
        var ct = TestContext.Current.CancellationToken;
        const int presentKey = 6031;
        const string value = "present";

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={presentKey} --key-class=java.lang.Integer --value={value} --value-class=java.lang.String",
            ct);

        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            var result = await region.GetAllAsync(
                new object[] { presentKey, 6032, 6033 }, ct);

            Assert.Equal(value, result[presentKey]);
            Assert.Null(result[6032]);
            Assert.Null(result[6033]);
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={presentKey} --key-class=java.lang.Integer",
                ct);
        }
    }
}
