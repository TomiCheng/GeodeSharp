using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.2 walking-skeleton check: the full consumer call chain
///
///   <c>cache.GetRegion&lt;int, byte[]&gt;("test").ContainsKeyAsync(...)</c>
///
/// runs end-to-end against a real Apache Geode server and returns
/// <c>false</c>. Exercises the entire path: build request via
/// <c>TcrMessageBuilder.ContainsKey</c>, dispatch via
/// <c>ThinClientPoolDM.SendSyncRequestAsync</c>, decode reply
/// (<c>Response</c> → bool via <c>SerializationRegistry</c>).
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionContainsKeyIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Path-(a) declarative config: one pool pointing at the fixture
    /// container, plus one region named "test" (which gfsh already
    /// pre-creates as REPLICATE inside the container). Pure defaults
    /// — no overrides, matches cppcache default usage.
    /// </summary>
    private void ConfigureCache(GeodeClientOptions config)
    {
        config.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "testPool",
                    Servers =
                    {
                        new CacheHostPortOptions
                        {
                            Host = fx.LocatorHost,
                            Port = fx.ServerPort,
                        },
                    },
                },
            },
            Regions =
            {
                new CacheRegionOptions
                {
                    Name = "test",
                    Attributes = { PoolName = "testPool" },
                },
            },
        };
    }

    [Fact]
    public async Task ContainsKeyAsync_returns_false_through_full_call_chain()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // Look up the XML-declared region. Returns null if init didn't
        // register it — that would be a wiring failure, not a server-
        // side problem.
        var region = cache.GetRegion<int, byte[]>("test");
        Assert.NotNull(region);

        Assert.False(await region.ContainsKeyAsync(123, cts.Token));

        await cache.CloseAsync(cts.Token);
    }
}
