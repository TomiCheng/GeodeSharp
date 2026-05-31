using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxy;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.CachingProxy.CachingProxyGetAsyncTests</c>.
/// Put populates server + local map; Get-after-Put should serve from the
/// local map (no wire), a never-put Get misses local then round-trips.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyGetAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(CachingProxyGetAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

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
        const string cacheName = nameof(CachingProxyGetAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

            Assert.Null(await region.GetAsync("get-never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
