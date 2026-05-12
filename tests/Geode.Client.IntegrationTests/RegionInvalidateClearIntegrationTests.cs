using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.3.a walking-skeleton end-to-end check for
/// <see cref="IRegion{TKey,TValue}.InvalidateAsync"/> and
/// <see cref="IRegion.ClearAsync"/> against a live Apache Geode server.
/// Scope mirrors <see cref="RegionCrudIntegrationTests"/>: <c>int</c>
/// keys + <c>int</c> values, single REPLICATE region <c>/test</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionInvalidateClearIntegrationTests(GeodeFixture fx)
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
    /// See <see cref="RegionCrudIntegrationTests"/> for the fresh-conn race
    /// rationale — same 3s settle delay applies.
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

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    // ====================================================================
    //  Invalidate
    // ====================================================================

    [Fact]
    public async Task Invalidate_keeps_key_but_clears_value()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            const int key = 2001;
            const int value = 555;

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));

            await region.InvalidateAsync(key, ct);

            // After invalidate: key stays, value is gone.
            Assert.True(await region.ContainsKeyAsync(key, ct));
            // GetAsync<int> on an invalidated entry: server replies
            // IsObject=0 + empty payload (or DSCode NullObj) — RegionView
            // unboxes null to default(int) == 0.
            Assert.Equal(0, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Invalidate_on_missing_key_does_not_throw()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // cppcache treats missing-key invalidate as success — Reply(6).
            const int missingKey = 0x7FFF_2001;
            await region.InvalidateAsync(missingKey, ct);
            Assert.False(await region.ContainsKeyAsync(missingKey, ct));
        }
    }

    [Fact]
    public async Task Put_after_Invalidate_restores_value()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            const int key = 2002;

            await region.PutAsync(key, 1, ct);
            await region.InvalidateAsync(key, ct);
            Assert.Equal(0, await region.GetAsync(key, ct));

            await region.PutAsync(key, 9, ct);
            Assert.Equal(9, await region.GetAsync(key, ct));
        }
    }

    // ====================================================================
    //  Clear
    // ====================================================================

    [Fact]
    public async Task Clear_removes_all_entries_but_keeps_region()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Picked in a distinct range from other tests in the collection.
            int[] keys = [3001, 3002, 3003];
            foreach (var k in keys)
            {
                await region.PutAsync(k, k * 10, ct);
            }
            foreach (var k in keys)
            {
                Assert.True(await region.ContainsKeyAsync(k, ct));
            }

            await region.ClearAsync(ct);

            // Region is intact; every key is gone.
            foreach (var k in keys)
            {
                Assert.False(await region.ContainsKeyAsync(k, ct));
            }

            // Region still usable — Put works after Clear.
            await region.PutAsync(3001, 7, ct);
            Assert.Equal(7, await region.GetAsync(3001, ct));
        }
    }

    [Fact]
    public async Task Clear_on_empty_region_does_not_throw()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Server accepts Clear on a region with nothing in it (or
            // nothing the client previously put). Reply(6) either way.
            // Note: this test doesn't pre-clear, so it observes whatever
            // state earlier tests in the collection left behind — we only
            // assert "Clear itself doesn't error".
            await region.ClearAsync(ct);
        }
    }
}
