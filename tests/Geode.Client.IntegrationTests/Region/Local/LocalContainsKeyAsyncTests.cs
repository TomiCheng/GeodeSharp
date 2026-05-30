using Xunit;

namespace Geode.Client.IntegrationTests.Region.Local;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.Local.LocalContainsKeyAsyncTests</c>.
/// Builds a pool against the real cluster <em>but</em> creates a
/// <see cref="RegionShortcut.Local"/> region (no <c>SetPoolName</c>) —
/// verifies that pool presence does not leak into local-shortcut
/// behaviour: Put / ContainsKey must stay pure-local with zero wire
/// activity (otherwise mis-routing would surface as a server-side write
/// or a wire-timeout).
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocalContainsKeyAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ContainsKeyAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(LocalContainsKeyAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.True(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyAsync_NeverPut_ReturnsFalse()
    {
        const string cacheName = nameof(LocalContainsKeyAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.False(await region.ContainsKeyAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
