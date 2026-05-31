using Xunit;

namespace Geode.Client.IntegrationTests.Region.Local;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.Local.LocalGetAsyncTests</c>. Pool exists
/// on the cache but the region is <see cref="RegionShortcut.Local"/> — Get
/// stays pure-local regardless.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocalGetAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(LocalGetAsyncTests) + "_afterput";
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

            Assert.Equal("v", await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task GetAsync_NeverPut_ReturnsNull()
    {
        const string cacheName = nameof(LocalGetAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Null(await region.GetAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
