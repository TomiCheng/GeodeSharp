using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.3.c walking-skeleton end-to-end check for
/// <see cref="IRegion{TKey,TValue}.GetAllAsync"/> against a live
/// Apache Geode server. Scope mirrors
/// <see cref="RegionRemoveAllIntegrationTests"/>: <c>int</c> keys +
/// <c>int</c> values, single REPLICATE region <c>/test</c>. Distinct
/// key range (5301-5599) keeps the suite parallel-safe against the
/// other collection members.
/// </summary>
/// <remarks>
/// First test in the suite that exercises
/// <see cref="Protocol.VersionedCacheableObjectPartList"/>'s
/// <c>hasObjects=true</c> + <c>_hasKeys=false</c> branch — the
/// chunked-reply decoder path that 1.3.b wrote but never hit
/// (RemoveAll's reply has both flags false).
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class RegionGetAllIntegrationTests(GeodeFixture fx)
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
    //  GetAll
    // ====================================================================

    [Fact]
    public async Task GetAll_returns_every_present_key()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Distinct key range. Seed values via single-key Put so the
            // GetAll round-trip's only job is reading.
            int[] keys = [5301, 5302, 5303, 5304];
            foreach (var k in keys)
            {
                await region.PutAsync(k, k * 100, ct);
            }

            var result = await region.GetAllAsync(keys, ct);

            // Each key returned with the matching seeded value. Result
            // dict element type is `int?` (TValue? = int?) — missing
            // server-side entries arrive as null; present entries as
            // the boxed int value.
            Assert.Equal(keys.Length, result.Count);
            foreach (var k in keys)
            {
                Assert.True(result.TryGetValue(k, out var v),
                    $"GetAll result missing key {k}");
                Assert.Equal(k * 100, v);
            }
        }
    }

    [Fact]
    public async Task GetAll_omits_missing_keys_from_typed_result()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Mix existing + never-put keys. cppcache wire-side
            // VersionedCacheableObjectPartList._byteArray[i]==3 flags
            // missing-on-server entries; ReadObjectPart stores null in
            // _values[key] for those slots. The typed RegionView layer
            // then skips null values (RegionView.GetAllAsync) so the
            // resulting IReadOnlyDictionary<int, int> only contains
            // present keys — caller uses ContainsKey/TryGetValue to
            // detect missing. See IRegion<TKey,TValue>.GetAllAsync
            // remarks for why nullable-int can't carry the missing
            // sentinel for unconstrained TValue.
            const int present = 5401;
            await region.PutAsync(present, 9999, ct);

            int[] mixed = [present, 0x7FFF_5402, 0x7FFF_5403];
            var result = await region.GetAllAsync(mixed, ct);

            Assert.Single(result);
            Assert.Equal(9999, result[present]);
            Assert.False(result.ContainsKey(0x7FFF_5402));
            Assert.False(result.ContainsKey(0x7FFF_5403));
        }
    }

    [Fact]
    public async Task GetAll_empty_keys_throws_argument_exception()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => region.GetAllAsync(Array.Empty<int>(), ct));
        }
    }
}
