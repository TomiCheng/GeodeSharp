using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxyEntryLru;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.CachingProxyEntryLru.CachingProxyEntryLruContainsKeyAsyncTests</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyEntryLruContainsKeyAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // 同 CachingProxy:Put 寫 server + local map。LRU 邊界對單 key 行為不變。
    [Fact]
    public async Task ContainsKeyAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(CachingProxyEntryLruContainsKeyAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxyEntryLru)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

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
        const string cacheName = nameof(CachingProxyEntryLruContainsKeyAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxyEntryLru)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

            Assert.False(await region.ContainsKeyAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
