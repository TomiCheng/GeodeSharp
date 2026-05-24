using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end check for <see cref="IRegion.ContainsKeyAsync"/> against a
/// real Apache Geode server: the unit tests lock the wire layout, this
/// file exercises the full call chain
/// (<see cref="RegionFactory.CreateAsync{TKey, TValue}(string, CancellationToken)"/>
/// → wire frame → server reply → bool decode). Keys are pre-populated
/// via <c>gfsh put</c> because <c>PutAsync</c> isn't implemented yet.
/// </summary>
/// <remarks>
/// Key range <c>9000s</c> picked to stay clear of other integration
/// suites' ranges. Tests in the shared <see cref="GeodeCollection"/>
/// run sequentially so the pre-put / remove pattern is race-free.
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class RegionContainsKeyIntegrationTests(GeodeFixture fx)
{
    private const string RegionName = "test";

    /// <summary>
    /// Cold-container ClientHealthMonitor registration race
    /// (<c>geode-fresh-conn-race.md</c>): the server takes 100ms–3s to
    /// register a fresh client connection. First op without this buffer
    /// surfaces as a <c>RegionDestroyedException</c>.
    /// </summary>
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

    [Fact]
    public async Task ContainsKeyAsync_AbsentKey_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        // 9001 belongs to a key range we never populate via gfsh.
        Assert.False(await region.ContainsKeyAsync(9001, ct));
    }

    [Fact]
    public async Task ContainsKeyAsync_PrePutKey_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        const int key = 9002;

        // Pre-populate via gfsh — PutAsync isn't implemented (Phase 1.x).
        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value=hello --value-class=java.lang.String",
            ct);
        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            Assert.True(await region.ContainsKeyAsync(key, ct));
        }
        finally
        {
            // Deterministic cleanup so re-runs against the same fixture
            // don't carry residue keys.
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }
}
