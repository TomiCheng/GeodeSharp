using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.4 end-to-end for the region OQL convenience methods —
/// <see cref="IRegion.ExistsValueAsync"/> and
/// <see cref="IRegion{TKey, TValue}.SelectValueAsync"/>. Covers the
/// implicit <c>SELECT DISTINCT * FROM /region this WHERE</c> wrap
/// (including the <c>this</c> alias) plus the 0 / 1 / &gt;1 cardinality
/// contract on <c>SelectValueAsync</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionQueryConvenienceIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

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
                        new CacheHostPortOptions { Host = fx.LocatorHost, Port = fx.ServerPort },
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

        // See RegionCrudIntegrationTests.FreshConnectionSettleDelay.
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    // ====================================================================
    //  ExistsValueAsync — true / false / argument validation
    // ====================================================================

    [Fact]
    public async Task ExistsValueAsync_returns_true_when_predicate_matches()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 97_000_000.. — value 97_111 keys 97_000_001.
            await region.PutAsync(97_000_001, 97_111, ct);

            Assert.True(await region.ExistsValueAsync("this = 97111", ct));
        }
    }

    [Fact]
    public async Task ExistsValueAsync_returns_false_when_no_match()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // No data populated in the 97_900_000.. value range.
            Assert.False(await region.ExistsValueAsync(
                "this >= 97900000 AND this <= 97999999", ct));
        }
    }

    [Fact]
    public async Task ExistsValueAsync_rejects_empty_predicate()
    {
        var (services, region, _, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                region.ExistsValueAsync("   ", cts.Token));
        }
    }

    // ====================================================================
    //  SelectValueAsync — 0 / 1 / >1 cardinality contract
    // ====================================================================

    [Fact]
    public async Task SelectValueAsync_returns_null_when_no_match()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // SelectValueAsync's "no match → null" contract is observable
            // through the non-typed IRegion surface (Task<object?>). The
            // typed IRegion<int,int> overlay collapses null to
            // default(int)=0 for value-type TValue — same generics gap as
            // GetAllAsync — so this assertion has to go through the base.
            IRegion baseRegion = region;
            var result = await baseRegion.SelectValueAsync(
                "this >= 97910000 AND this <= 97919999", ct);

            Assert.Null(result);
        }
    }

    [Fact]
    public async Task SelectValueAsync_returns_value_when_exactly_one_match()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 97_200_000.. — only one value (97_222) matches.
            await region.PutAsync(97_200_001, 97_222, ct);

            var result = await region.SelectValueAsync("this = 97222", ct);

            Assert.Equal(97_222, result);
        }
    }

    [Fact]
    public async Task SelectValueAsync_throws_when_more_than_one_match()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 97_300_000.. — three distinct values land in
            // the 97_300..97_309 band so DISTINCT *'s row set has size 3.
            await region.PutAsync(97_300_001, 97_301, ct);
            await region.PutAsync(97_300_002, 97_302, ct);
            await region.PutAsync(97_300_003, 97_303, ct);

            var ex = await Assert.ThrowsAsync<GeodeException>(() =>
                region.SelectValueAsync("this >= 97301 AND this <= 97303", ct));

            // cppcache / Java parity — message carries the actual count.
            Assert.Contains("more than one result", ex.Message);
            Assert.Contains("3", ex.Message);
        }
    }

    // ====================================================================
    //  `this` alias regression — verifies the FROM-clause `this`
    //  declaration the client injects actually resolves server-side.
    // ====================================================================

    [Fact]
    public async Task ExistsValueAsync_resolves_this_alias_via_implicit_FROM_clause()
    {
        var (services, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 97_500_000.. — value 97_555 keys 97_500_001.
            // Predicate uses `this = scalar`, which only resolves when
            // the client prepends `... FROM /test this WHERE ...`. If
            // the alias is missing, server rejects with QueryException.
            await region.PutAsync(97_500_001, 97_555, ct);

            Assert.True(await region.ExistsValueAsync("this = 97555", ct));
        }
    }
}
