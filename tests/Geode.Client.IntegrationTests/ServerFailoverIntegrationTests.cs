using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end coverage for <see cref="Internal.ThinClientPoolDM"/>'s
/// <c>SendSyncRequestCoreAsync</c> retry frame (Steps A-G,
/// <c>ThinClientPoolDM.cs</c>): a server is taken down mid-session
/// and subsequent ops must succeed via failover to one of the
/// remaining servers. Without this test the retry / <c>excludeServers</c>
/// path is only exercised by unit fakes; the success-path locator
/// round-trip in <see cref="LocatorModeIntegrationTests"/> never
/// observes a real transport failure.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ServerFailoverIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);
    private const string RegionName = "test";

    [Fact]
    public async Task Ops_succeed_via_failover_after_one_server_is_stopped()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddGeodeFactory()
            .BuildServiceProvider();

        var cache = await services.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", cts.Token);

        // Pool MUST be locator-mode so the retry frame's
        // SelectEndpointAsync goes through the locator and can pick a
        // different server when the prior one is excluded. Static-server
        // lists would defeat the test (no failover path exercised).
        //
        // Disable the periodic locator-list refresh: the per-request
        // excludeServers carries srv1 quarantine through the retry frame
        // already, so refresh adds no value here and just opens a race
        // window if the peer list returned by the locator hasn't fully
        // propagated the srv1 stop event yet.
        await cache.PoolManager.CreateFactory()
            .AddLocator(fx.LocatorHost, fx.LocatorPort)
            .AddLocator(fx.LocatorHost, fx.LocatorPort2)
            .SetUpdateLocatorListInterval(TimeSpan.Zero)
            .SetMinConnections(1)
            .BuildAsync("default", cts.Token);

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<int, int>(RegionName, cts.Token);

        // Sanity round trip on the full 3-server cluster.
        const int sentinel = 0x6000_0001;
        await region.PutAsync(sentinel, -1, cts.Token);
        Assert.Equal(-1, await region.GetAsync(sentinel, cts.Token));

        try
        {
            // Step 1 — knock srv1 over from outside. The client has no
            // way of knowing in advance which server the locator pinned
            // its cached connection to; killing srv1 unconditionally
            // ensures that AT LEAST one in-flight op below will either
            // (a) inherit the broken cached conn and fall into the
            // SendSyncRequestCoreAsync catch, or (b) have the locator
            // hand it back srv1 (membership lag) and fall into the same
            // catch on connect-refused. Either path proves the retry
            // frame fires.
            await fx.GfshAsync("stop server --name=srv1", cts.Token);

            // Step 2 — drive enough traffic that the locator round-robin
            // has multiple chances to return srv1, and any cached conn
            // to srv1 gets exercised. 30 keys × 2 ops = 60 hits; any
            // unhandled socket / connection-refused that escapes the
            // retry frame surfaces as a test failure here.
            const int baseKey = 0x6000_1000;
            for (var i = 0; i < 30; i++)
            {
                await region.PutAsync(baseKey + i, i * 2, cts.Token);
            }
            for (var i = 0; i < 30; i++)
            {
                Assert.Equal(i * 2, await region.GetAsync(baseKey + i, cts.Token));
            }
        }
        finally
        {
            // Restart srv1 so downstream tests in the same collection
            // fixture see the full 3-server topology again.
            // --dir is left at gfsh's default; membership rejoin only
            // needs --locators, log file location is incidental.
            await fx.GfshAsync(
                $"start server --name=srv1 --server-port={fx.ServerPort} "
                + "--hostname-for-clients=localhost "
                + $"--locators=localhost[{fx.LocatorPort}],localhost[{fx.LocatorPort2}]",
                cts.Token);
        }

        await cache.CloseAsync(cts.Token);
    }
}
