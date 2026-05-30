using Xunit;

namespace Geode.Client.IntegrationTests.Region.Local;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.Local.LocalContainsKeyOnServerAsyncTests</c>.
/// Pool is built against the real cluster <em>but</em> the region uses
/// <see cref="RegionShortcut.Local"/> — <c>ContainsKeyOnServerAsync</c>
/// must still throw <see cref="NotSupportedException"/> regardless of
/// whether a pool happens to exist on the cache.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocalContainsKeyOnServerAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_Throws()
    {
        const string cacheName = nameof(LocalContainsKeyOnServerAsyncTests) + "_afterput";
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

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyOnServerAsync_NeverPut_Throws()
    {
        const string cacheName = nameof(LocalContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
