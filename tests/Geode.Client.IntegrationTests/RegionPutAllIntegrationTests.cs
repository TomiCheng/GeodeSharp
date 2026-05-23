/*
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.3.c walking-skeleton end-to-end check for
/// <see cref="IRegion{TKey,TValue}.PutAllAsync"/> against a live
/// Apache Geode server. Scope mirrors
/// <see cref="RegionRemoveAllIntegrationTests"/>: <c>int</c> keys +
/// <c>int</c> values, single REPLICATE region <c>/test</c>. Distinct
/// key range (5001-5299) keeps the suite parallel-safe against the
/// other collection members.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionPutAllIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private const string RegionName = "test";

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
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    // ====================================================================
    //  PutAll
    // ====================================================================

    [Fact]
    public async Task PutAll_writes_every_entry_in_one_round_trip()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Distinct key range from other tests in the collection.
            var entries = new Dictionary<int, int>
            {
                [5001] = 50_010,
                [5002] = 50_020,
                [5003] = 50_030,
                [5004] = 50_040,
            };

            await region.PutAllAsync(entries, ct);

            foreach (var (k, v) in entries)
            {
                Assert.True(await region.ContainsKeyAsync(k, ct));
                Assert.Equal(v, await region.GetAsync(k, ct));
            }
        }
    }

    [Fact]
    public async Task PutAll_overwrites_existing_entries()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Seed three keys with sentinel values via single-key Put;
            // then PutAll over them with new values. The bulk path must
            // overwrite cleanly (same wire op as single Put on the server).
            await region.PutAsync(5101, 1, ct);
            await region.PutAsync(5102, 2, ct);
            await region.PutAsync(5103, 3, ct);

            var entries = new Dictionary<int, int>
            {
                [5101] = 5_101_999,
                [5102] = 5_102_999,
                [5103] = 5_103_999,
            };
            await region.PutAllAsync(entries, ct);

            Assert.Equal(5_101_999, await region.GetAsync(5101, ct));
            Assert.Equal(5_102_999, await region.GetAsync(5102, ct));
            Assert.Equal(5_103_999, await region.GetAsync(5103, ct));
        }
    }

    [Fact]
    public async Task PutAll_empty_map_throws_argument_exception()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => region.PutAllAsync(new Dictionary<int, int>(), ct));
        }
    }
}

*/