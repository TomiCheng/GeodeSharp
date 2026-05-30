using Xunit;

namespace Geode.Client.Tests.Region.Proxy;

/// <summary>
/// <see cref="IRegion.ContainsKeyOnServerAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Proxy"/>.
/// </summary>
public class ProxyContainsKeyOnServerAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Proxy (CachingEnabled=false):Put 寫到 server,本地沒 map。
    // ContainsKeyAsync 因為 LocalRegion.cpp:822 短路永遠回 false;
    // ContainsKeyOnServerAsync 走 wire,真的問 server,所以拿得到 true —
    // 這個 API 才能看到 server ground truth。對映 cppcache
    // ThinClientRegion::containsKeyOnServer (ThinClientRegion.cpp:676)。
    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(ProxyContainsKeyOnServerAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await ServerOptional.RunAsync(async () =>
            {
                await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

                Assert.True(await region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyOnServerAsync_NeverPut_ReturnsFalse()
    {
        const string cacheName = nameof(ProxyContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await ServerOptional.RunAsync(async () =>
            {
                Assert.False(await region.ContainsKeyOnServerAsync("never-put", ct: TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
