using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly GeodeFixture _fx = fx;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);
    private const string RegionName = "test";

    [Fact]
    public async Task Ops_succeed_via_failover_after_one_server_is_stopped()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    // Pool MUST be locator-mode so the retry frame's
                    // SelectEndpointAsync goes through the locator and
                    // can pick a different server when the prior one
                    // is excluded. Static-server lists would defeat
                    // the test (no failover path exercised).
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
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.LocatorPort2,
                            },
                        },
                        // Disable the periodic locator-list refresh: the
                        // per-request excludeServers carries srv1 quarantine
                        // through the retry frame already, so refresh adds
                        // no value here and just opens a race window if the
                        // peer list returned by the locator hasn't fully
                        // propagated the srv1 stop event yet.
                        UpdateLocatorListInterval = TimeSpan.Zero,
                    },
                },
                Regions =
                {
                    new CacheRegionOptions { Name = RegionName },
                },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

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
            await _fx.GfshAsync("stop server --name=srv1", cts.Token);

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
            // Restart srv1 so any downstream tests in the same
            // collection-fixture run see the full 3-server topology.
            // Stop is permanent until the container is recycled, so the
            // restart has to happen even on test failure — hence the
            // try / finally rather than a separate Dispose path.
            //
            // --dir is left at gfsh's default (the container's working
            // directory at exec time) since `_container.ExecAsync`
            // doesn't carry the original `cd /work` from fixture init;
            // membership rejoin only needs --locators, not the file
            // layout, and the log file location for the relaunched srv1
            // is incidental to test correctness.
            await _fx.GfshAsync(
                $"start server --name=srv1 --server-port={_fx.ServerPort} "
                + "--hostname-for-clients=localhost "
                + $"--locators=localhost[{_fx.LocatorPort}],localhost[{_fx.LocatorPort2}]",
                cts.Token);
        }

        await cache.CloseAsync(cts.Token);
    }
}
