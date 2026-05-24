using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end checks for the three Region mutation ops that don't have
/// their own integration file yet: <see cref="IRegion.ClearAsync"/>,
/// <see cref="IRegion.InvalidateAsync(object, CancellationToken)"/>,
/// <see cref="IRegion.RemoveAsync(object, CancellationToken)"/>.
/// Key range <c>7000s</c> to stay clear of other suites.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class RegionClearInvalidateRemoveIntegrationTests(GeodeFixture fx)
{
    private const string RegionName = "test";

    /// <summary>cppcache <c>geode-fresh-conn-race.md</c> mitigation — 3s buffer.</summary>
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

    // ── Remove ────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_PrePutKey_ReturnsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        const int key = 7001;

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value=v --value-class=java.lang.String",
            ct);

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        Assert.True(await region.RemoveAsync(key, ct));
        // Sanity: re-removing reports false (no cleanup needed in finally).
        Assert.False(await region.RemoveAsync(key, ct));
    }

    [Fact]
    public async Task RemoveAsync_AbsentKey_ReturnsFalse()
    {
        // cppcache TcrMessage.cpp:1317 — reply's last part i32 entryNotFound=1.
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        Assert.False(await region.RemoveAsync(7002, ct));
    }

    // ── Invalidate ────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateAsync_PrePutKey_KeyRetained_ValueGone()
    {
        // After invalidate, server still has the key (ContainsKey=true)
        // but Get returns null. cppcache parity:
        // ThinClientRegion::invalidateNoThrow_remote success path.
        var ct = TestContext.Current.CancellationToken;
        const int key = 7003;

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={key} --key-class=java.lang.Integer --value=v --value-class=java.lang.String",
            ct);
        try
        {
            await using var sp = BuildSp();
            var region = await BuildRegionAsync(sp, ct);

            await region.InvalidateAsync(key, ct);

            Assert.True(await region.ContainsKeyAsync(key, ct));  // key still present
            Assert.Null(await region.GetAsync(key, ct));          // value null
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    // ── Clear ─────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        // Pre-populate two keys via gfsh; client Clear; verify both are
        // gone (Get returns null). cppcache parity: server-side full
        // region clear, no version-tag handling Phase 1.x.
        var ct = TestContext.Current.CancellationToken;
        const int keyA = 7004;
        const int keyB = 7005;

        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={keyA} --key-class=java.lang.Integer --value=a --value-class=java.lang.String",
            ct);
        await fx.GfshAsync(
            $"put --region=/{RegionName} --key={keyB} --key-class=java.lang.Integer --value=b --value-class=java.lang.String",
            ct);

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        await region.ClearAsync(ct);

        Assert.Null(await region.GetAsync(keyA, ct));
        Assert.Null(await region.GetAsync(keyB, ct));
    }
}
