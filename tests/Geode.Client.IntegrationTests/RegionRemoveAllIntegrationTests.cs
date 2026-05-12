using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.3.b walking-skeleton end-to-end check for
/// <see cref="IRegion{TKey,TValue}.RemoveAllAsync"/> against a live
/// Apache Geode server. Scope mirrors
/// <see cref="RegionInvalidateClearIntegrationTests"/>: <c>int</c>
/// keys + <c>int</c> values, single REPLICATE region <c>/test</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionRemoveAllIntegrationTests(GeodeFixture fx)
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
    //  RemoveAll
    // ====================================================================

    [Fact]
    public async Task RemoveAll_drops_every_key_in_one_round_trip()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Distinct key range from other tests in the collection.
            int[] keys = [4001, 4002, 4003, 4004];
            foreach (var k in keys)
            {
                await region.PutAsync(k, k * 10, ct);
            }
            foreach (var k in keys)
            {
                Assert.True(await region.ContainsKeyAsync(k, ct));
            }

            await region.RemoveAllAsync(keys, ct);

            foreach (var k in keys)
            {
                Assert.False(await region.ContainsKeyAsync(k, ct));
            }
        }
    }

    [Fact]
    public async Task RemoveAll_tolerates_missing_keys()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Mix existing + never-put keys. cppcache RemoveAll reports
            // misses via VersionedCacheableObjectPartList.byteArray[i]==3;
            // Phase 1.3 drops per-key result on the floor — the call
            // succeeds either way and present keys end up gone.
            const int present = 4101;
            await region.PutAsync(present, 99, ct);

            int[] mixed = [present, 0x7FFF_4102, 0x7FFF_4103];
            await region.RemoveAllAsync(mixed, ct);

            Assert.False(await region.ContainsKeyAsync(present, ct));
        }
    }

    [Fact]
    public async Task RemoveAll_empty_keys_throws_argument_exception()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => region.RemoveAllAsync(Array.Empty<int>(), ct));
        }
    }

    [Fact]
    public async Task RemoveAll_null_keys_throws_argument_null()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => region.RemoveAllAsync(null!, ct));
        }
    }

    [Fact]
    public async Task RemoveAll_single_key_round_trip()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Smallest non-empty batch — exercises the same chunked-reply
            // path with N=1 (EventIdGenerator.NextRange(1) edge case).
            const int key = 4201;
            await region.PutAsync(key, 42, ct);
            Assert.True(await region.ContainsKeyAsync(key, ct));

            await region.RemoveAllAsync([key], ct);

            Assert.False(await region.ContainsKeyAsync(key, ct));
        }
    }
}
