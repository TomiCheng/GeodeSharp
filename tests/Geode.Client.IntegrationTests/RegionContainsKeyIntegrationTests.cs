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
/// <c>false</c> without throwing. The op body itself is a stub
/// (<see cref="Geode.Client.Services.ThinClientRegion.ContainsKeyAsync"/>
/// returns <c>Task.FromResult(false)</c>); the goal here is to prove
/// the wiring is correct so the next change — wiring the actual
/// <c>MessageType.ContainsKey(38)</c> request — has a known-good
/// scaffold to drop into.
/// </summary>
/// <remarks>
/// Path exercised:
/// <list type="number">
///   <item><c>EnsureInitializedAsync</c> runs path (a): builds pool,
///         init-handshakes against the fixture container, then builds
///         the XML-declared region into <c>_regions["test"]</c>.</item>
///   <item><c>Cache.GetRegion(string)</c> finds the registered
///         region; <c>GetRegion&lt;int, byte[]&gt;(string)</c> wraps
///         it in a fresh <c>RegionView&lt;int, byte[]&gt;</c>.</item>
///   <item><c>RegionView.ContainsKeyAsync(int)</c> boxes the key and
///         forwards to the inner <c>IRegion</c>.</item>
///   <item><c>ThinClientRegion.ContainsKeyAsync(object)</c> stub
///         returns <c>false</c>.</item>
/// </list>
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class RegionContainsKeyIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Path-(a) declarative config: one pool pointing at the fixture
    /// container, plus one region named "test" (which gfsh already
    /// pre-creates as REPLICATE inside the container).
    /// </summary>
    private void ConfigureCacheXml(GeodeClientOptions config)
    {
        config.CacheXml = new CacheXmlOptions
        {
            Pools =
            {
                new CacheXmlPoolOptions
                {
                    Name = "testPool",
                    Servers =
                    {
                        new CacheXmlHostPort
                        {
                            Host = fx.LocatorHost,
                            Port = fx.ServerPort,
                        },
                    },
                },
            },
            Regions =
            {
                new CacheXmlRegionOptions
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
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCache>();
        await cache.EnsureInitializedAsync(cts.Token);

        // Look up the XML-declared region. Returns null if init didn't
        // register it — that would be a wiring failure, not a server-
        // side problem.
        var region = cache.GetRegion<int, byte[]>("test");
        Assert.NotNull(region);

        // Walking-skeleton stub: any key → false, no wire op, no throw.
        // Replace this expectation with `true` (after a matching Put)
        // once Phase 1.2.e wires the real ContainsKey(38) request.
        Assert.False(await region.ContainsKeyAsync(123, cts.Token));

        await cache.CloseAsync(cts.Token);
    }
}
