using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end checks for the two OQL-backed region convenience methods:
/// <see cref="IRegion.ExistsValueAsync(string, CancellationToken)"/> and
/// <see cref="IRegion.SelectValueAsync(string, CancellationToken)"/>.
/// Wire path: ThinClientRegion.QueryAsync → RemoteQueryService →
/// RemoteQuery → MessageType.Query (or QueryWithParameters) →
/// ChunkedQueryResponse.
/// Key range <c>5000s</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionExistsSelectValueIntegrationTests(GeodeFixture fx)
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

    // ── ExistsValueAsync ──────────────────────────────────────────

    [Fact]
    public async Task ExistsValueAsync_PrePutMatch_ReturnsTrue()
    {
        // Pre-put a row, then query for any row where value=hit.
        // QueryAsync wraps the predicate as:
        //   select distinct * from /test this where this='hit'
        // matching cppcache Region::query (ThinClientRegion.cpp:524-535).
        var ct = TestContext.Current.CancellationToken;
        const int key = 5001;
        const string value = "hit";

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value={value} --value-class=java.lang.String",
            ct);
        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            Assert.True(await region.ExistsValueAsync($"this='{value}'", ct));
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    [Fact]
    public async Task ExistsValueAsync_NoMatch_ReturnsFalse()
    {
        // No setup; predicate matches no rows in the region.
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        Assert.False(await region.ExistsValueAsync("this='no-such-value-5002'", ct));
    }

    // ── SelectValueAsync ──────────────────────────────────────────

    [Fact]
    public async Task SelectValueAsync_SingleMatch_ReturnsValue()
    {
        // SelectValue contract: 0 matches → null, 1 match → that value,
        // >1 match → throw. Phase 1.x scope locks the 1-match path here.
        var ct = TestContext.Current.CancellationToken;
        const int key = 5003;
        const string value = "unique-5003";

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value={value} --value-class=java.lang.String",
            ct);
        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            var result = await region.SelectValueAsync($"this='{value}'", ct);

            Assert.Equal(value, result);
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    [Fact]
    public async Task SelectValueAsync_NoMatch_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        Assert.Null(await region.SelectValueAsync("this='no-such-value-5004'", ct));
    }
}
