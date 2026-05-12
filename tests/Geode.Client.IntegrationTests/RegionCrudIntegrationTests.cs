using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.2 walking-skeleton end-to-end check for all four basic
/// region operations against a live Apache Geode server:
///
/// <list type="bullet">
///   <item><see cref="IRegion{TKey,TValue}.PutAsync"/></item>
///   <item><see cref="IRegion{TKey,TValue}.GetAsync"/></item>
///   <item><see cref="IRegion{TKey,TValue}.ContainsKeyAsync"/></item>
///   <item><see cref="IRegion{TKey,TValue}.RemoveAsync"/></item>
/// </list>
///
/// Scope is intentionally narrow: <c>int</c> keys + <c>int</c> values,
/// matching the only converters the registry currently ships
/// (<c>Int32DataConverter</c>, <c>BooleanDataConverter</c>). String /
/// byte[] / collection coverage lands as their converters do.
/// </summary>
/// <remarks>
/// <para>
/// All tests in this file share one <see cref="GeodeFixture"/> via the
/// xUnit collection — the container starts once per test run, with
/// region <c>/test</c> pre-created as <c>REPLICATE</c> by gfsh.
/// </para>
/// <para>
/// <b>Key uniqueness across tests.</b> Each test picks its own key in
/// a distinct range so they can't step on each other if xUnit decides
/// to run them in parallel and the test container is reused. The Geode
/// server itself dedups Put events by <c>(clientId, threadId, seq)</c>,
/// but the test's correctness checks (ContainsKey true/false) are
/// observational and would race on a shared key.
/// </para>
/// <para>
/// <b>Known carry-over from Phase 1.1.</b> The "fresh-conn race" —
/// server-side <c>ClientHealthMonitor</c> registration lag — can still
/// surface a one-off <c>RegionDestroyedException</c> on the very first
/// op against a freshly-warmed pool (5-100ms cold-JVM window). The
/// dedicated mitigation (pool warmup or readiness probe) is tracked
/// separately; if these tests flake transiently with that exact
/// exception, that's the cause — not a wire-layer regression.
/// </para>
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class RegionCrudIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private const string RegionName = "test";

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
                    Name = RegionName,
                    Attributes = { PoolName = "testPool" },
                },
            },
        };
    }

    /// <summary>
    /// In-test mitigation for the Phase 1.1 carry-over "fresh-conn race":
    /// the server's per-connection <c>ClientHealthMonitor</c>
    /// registration runs asynchronously after the handshake completes
    /// (5-100ms on a cold JVM). The first user op against a brand-new
    /// connection inside that window can surface as
    /// <c>RegionDestroyedException</c> even though gfsh did create the
    /// region. A short sleep after <c>EnsureInitializedAsync</c>
    /// returns lets the server-side registration settle. The proper
    /// fix (pool warmup or server-side readiness probe) lands as a
    /// separate item; this delay is the duct-tape until then.
    /// </summary>
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

    private async Task<(ServiceProvider Services, IRegion<int, int> Region, CancellationToken Ct, CancellationTokenSource Cts)>
        OpenAsync()
    {
        var cts = new CancellationTokenSource(TestTimeout);

        var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCache>();
        await cache.EnsureInitializedAsync(cts.Token);

        // See FreshConnectionSettleDelay xmldoc.
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    [Fact]
    public async Task Put_then_Get_round_trips_int32_value()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            const int key = 1001;
            const int value = 42;

            await region.PutAsync(key, value, ct);
            var actual = await region.GetAsync(key, ct);

            Assert.Equal(value, actual);
        }
    }

    [Fact]
    public async Task Get_returns_default_for_missing_key()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Key picked in a high range to avoid collision with any
            // value any other test in this collection might have put.
            const int missingKey = 0x7FFF_0001;

            // GetAsync<int> on a missing key: server replies with
            // IsObject=0 + empty payload, RegionView unboxes null to
            // default(int) == 0.
            var actual = await region.GetAsync(missingKey, ct);
            Assert.Equal(0, actual);
        }
    }

    [Fact]
    public async Task ContainsKeyAsync_tracks_Put_then_Remove()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            const int key = 1002;

            // Pre-state: not there yet.
            Assert.False(await region.ContainsKeyAsync(key, ct));

            // Put → present.
            await region.PutAsync(key, 7, ct);
            Assert.True(await region.ContainsKeyAsync(key, ct));

            // Remove → gone again. RemoveAsync returns true because the
            // entry existed.
            Assert.True(await region.RemoveAsync(key, ct));
            Assert.False(await region.ContainsKeyAsync(key, ct));
        }
    }

    [Fact]
    public async Task RemoveAsync_returns_false_when_key_absent()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Server replies REPLY with entryNotFound=1.
            const int missingKey = 0x7FFF_0002;
            Assert.False(await region.RemoveAsync(missingKey, ct));
        }
    }

    [Fact]
    public async Task Put_overwrites_existing_value()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            const int key = 1003;

            await region.PutAsync(key, 100, ct);
            await region.PutAsync(key, 200, ct);

            Assert.Equal(200, await region.GetAsync(key, ct));
        }
    }
}
