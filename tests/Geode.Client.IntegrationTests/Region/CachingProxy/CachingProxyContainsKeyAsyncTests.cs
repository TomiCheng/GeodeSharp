using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxy;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.CachingProxy.CachingProxyContainsKeyAsyncTests</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyContainsKeyAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // CachingProxy: Put 寫 server + local map,ContainsKeyAsync 純本地查
    // (local map 已被 Put 填),回 true。Real server here actually accepts
    // the Put — wire round-trip completes; local map gets populated as a
    // side effect; ContainsKeyAsync sees the entry locally.
    [Fact]
    public async Task ContainsKeyAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(CachingProxyContainsKeyAsyncTests) + "_afterput";
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
        const string cacheName = nameof(CachingProxyContainsKeyAsyncTests) + "_neverput";
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

            Assert.False(await region.ContainsKeyAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
