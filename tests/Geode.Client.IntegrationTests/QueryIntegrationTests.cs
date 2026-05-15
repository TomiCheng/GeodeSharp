using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.4 end-to-end smoke for the OQL query path against a live
/// Apache Geode server. Covers <c>SELECT *</c>, <c>SELECT COUNT(*)</c>,
/// and parameterised filtering — all single-column / bucket-1 wire
/// shapes. Multi-column StructSet (<c>SELECT id, name</c>) needs
/// PDX-stored values on the server side and lives in a separate test
/// once PDX server-side population is wired.
/// </summary>
/// <remarks>
/// Tests share the fixture's <c>/test</c> region with other integration
/// suites. Each test picks a unique value range and filters via
/// <c>WHERE this BETWEEN $low AND $high</c> to isolate itself from
/// leftover data from prior tests in the same collection.
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class QueryIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

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

    private async Task<(ServiceProvider Services, IGeodeCache Cache, IRegion<int, int> Region, CancellationToken Ct, CancellationTokenSource Cts)>
        OpenAsync()
    {
        var cts = new CancellationTokenSource(TestTimeout);

        var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // See RegionCrudIntegrationTests.FreshConnectionSettleDelay.
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, int>(RegionName);
        Assert.NotNull(region);

        return (services, cache, region, cts.Token, cts);
    }

    // ====================================================================
    //  SELECT * — single-column ResultSet path (C11a / C11b ResultSet)
    // ====================================================================

    [Fact]
    public async Task SelectStar_returns_values_matching_predicate()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range for this test: 91_000_000..91_999_999.
            await region.PutAsync(91_000_001, 91_100, ct);
            await region.PutAsync(91_000_002, 91_200, ct);
            await region.PutAsync(91_000_003, 91_300, ct);

            var rows = await cache.GetQueryService()
                .NewQuery<int>("SELECT t FROM /test t WHERE t >= 91100 AND t <= 91300")
                .ExecuteAsync(ct);

            Assert.Equal(3, rows.Count);
            Assert.Contains(91_100, rows);
            Assert.Contains(91_200, rows);
            Assert.Contains(91_300, rows);
        }
    }

    [Fact]
    public async Task SelectStar_with_no_match_returns_empty()
    {
        var (services, cache, _, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Range no test populates.
            var rows = await cache.GetQueryService()
                .NewQuery<int>(
                    "SELECT t FROM /test t WHERE t >= 999000000 AND t <= 999999999")
                .ExecuteAsync(ct);

            Assert.Empty(rows);
        }
    }

    // ====================================================================
    //  SELECT COUNT(*) — scalar / NullObject chunk path (C3b)
    // ====================================================================

    [Fact]
    public async Task SelectCount_returns_matching_row_count()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 92_000_000..
            for (var i = 0; i < 5; i++)
            {
                await region.PutAsync(92_000_001 + i, 92_500 + i, ct);
            }

            var rows = await cache.GetQueryService()
                .NewQuery<int>(
                    "SELECT COUNT(*) FROM /test t WHERE t >= 92500 AND t <= 92504")
                .ExecuteAsync(ct);

            // COUNT(*) returns a single-element list with the count.
            Assert.Single(rows);
            Assert.Equal(5, rows[0]);
        }
    }

    [Fact]
    public async Task ExecuteSingleAsync_unwraps_count_scalar()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 93_000_000..
            for (var i = 0; i < 3; i++)
            {
                await region.PutAsync(93_000_001 + i, 93_500 + i, ct);
            }

            var count = await cache.GetQueryService()
                .NewQuery<int>(
                    "SELECT COUNT(*) FROM /test t WHERE t >= 93500 AND t <= 93502")
                .ExecuteSingleAsync(ct);

            Assert.Equal(3, count);
        }
    }

    // ====================================================================
    //  Parameterised query — QueryWithParameters(80) wire path
    // ====================================================================

    [Fact]
    public async Task QueryWithParameters_filters_via_bind_value()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 94_000_000..
            await region.PutAsync(94_000_001, 94_100, ct);
            await region.PutAsync(94_000_002, 94_200, ct);
            await region.PutAsync(94_000_003, 94_300, ct);

            // Use $1 + $2 to bound the range — covers param positional binding
            // and that QueryWithParameters(80) wire path is reached.
            var rows = await cache.GetQueryService()
                .NewQuery<int>("SELECT t FROM /test t WHERE t >= $1 AND t <= $2")
                .WithParameters(94_150, 94_250)
                .ExecuteAsync(ct);

            Assert.Single(rows);
            Assert.Equal(94_200, rows[0]);
        }
    }

    [Fact]
    public async Task QueryWithParameters_count_combines_with_extension()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 95_000_000..
            for (var i = 0; i < 4; i++)
            {
                await region.PutAsync(95_000_001 + i, 95_500 + i, ct);
            }

            // Parameterised COUNT + ExecuteSingleAsync to verify both
            // QueryWithParameters(80) wire and the scalar extension
            // compose end-to-end.
            var count = await cache.GetQueryService()
                .NewQuery<int>(
                    "SELECT COUNT(*) FROM /test t WHERE t >= $1 AND t <= $2")
                .WithParameters(95_500, 95_503)
                .ExecuteSingleAsync(ct);

            Assert.Equal(4, count);
        }
    }

    // ====================================================================
    //  Type mismatch — IQuery<wrong T> surfaces as InvalidCastException
    // ====================================================================

    [Fact]
    public async Task IQuery_with_wrong_T_throws_InvalidCastException()
    {
        var (services, cache, region, ct, cts) = await OpenAsync();
        await using (services)
        using (cts)
        {
            // Unique range 96_000_000.. — int values.
            await region.PutAsync(96_000_001, 96_500, ct);

            // Caller asked for string rows but the values are int —
            // collector's (T?)scalar cast throws at decode time.
            await Assert.ThrowsAsync<InvalidCastException>(async () =>
            {
                _ = await cache.GetQueryService()
                    .NewQuery<string>("SELECT t FROM /test t WHERE t = 96500")
                    .ExecuteAsync(ct);
            });
        }
    }
}
