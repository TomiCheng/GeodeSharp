using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end Put + Get round-trip against a real Apache Geode server.
/// Client-only round-trip (Put → Get) checks the encode/decode pair;
/// gfsh cross-checks confirm the server actually parsed our bytes
/// rather than just symmetrically echoing them back (memory note in
/// <c>PROGRESS.md</c> Phase 1.3.0).
/// </summary>
/// <remarks>
/// Key range <c>8000s</c> picked to stay clear of the ContainsKey
/// suite's 9000s and other ranges. Tests in the shared
/// <see cref="GeodeCollection"/> run sequentially so put / remove
/// pattern is race-free.
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class RegionPutGetIntegrationTests(GeodeFixture fx)
{
    private const string RegionName = "test";

    /// <summary>cppcache <c>geode-fresh-conn-race.md</c> mitigation — 3s buffer for ClientHealthMonitor.</summary>
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private async Task<IRegion<int, string>> BuildRegionAsync(ServiceProvider sp, CancellationToken ct)
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .BuildAsync("p", ct);

        await Task.Delay(FreshConnectionSettleDelay, ct);

        return await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<int, string>(RegionName, ct);
    }

    // ── Client-only round-trip ────────────────────────────────────

    [Fact]
    public async Task PutThenGet_RoundTripsValue()
    {
        var ct = TestContext.Current.CancellationToken;
        const int key = 8001;
        const string value = "round-trip-payload";

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        try
        {
            await region.PutAsync(key, value, ct);
            var got = await region.GetAsync(key, ct);

            Assert.Equal(value, got);
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    [Fact]
    public async Task Put_PersistsOnServer_VisibleViaGfsh()
    {
        // gfsh cross-check: prove the server deserialized our wire bytes
        // into the expected Java types (Integer key, String value) rather
        // than just round-tripping opaque bytes.
        var ct = TestContext.Current.CancellationToken;
        const int key = 8002;
        const string value = "hello-gfsh";

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        try
        {
            await region.PutAsync(key, value, ct);

            var gfshOutput = await fx.GfshAsync(
                $"get --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);

            Assert.Contains(value, gfshOutput);
            Assert.Contains("java.lang.String", gfshOutput);
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    // ── Get cache-miss path ──────────────────────────────────────

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        // cppcache readObjectPart empty branch: server returns Response
        // with single Part(IsObject=0, payloadLength=0) when the key is
        // absent; DecodeValuePart maps that to null.
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        Assert.Null(await region.GetAsync(8003, ct));
    }

    // ── Get of gfsh-prepopulated value ───────────────────────────

    [Fact]
    public async Task GetAsync_GfshPrePut_DecodesValue()
    {
        // Reverse cross-check: server populates, client decodes. Catches
        // SerializationRegistry-side decoder regressions that a client-
        // only round-trip would miss (symmetric encode/decode bug).
        var ct = TestContext.Current.CancellationToken;
        const int key = 8004;
        const string value = "gfsh-prepopulated";

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value={value} --value-class=java.lang.String",
            ct);
        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            Assert.Equal(value, await region.GetAsync(key, ct));
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }
}
