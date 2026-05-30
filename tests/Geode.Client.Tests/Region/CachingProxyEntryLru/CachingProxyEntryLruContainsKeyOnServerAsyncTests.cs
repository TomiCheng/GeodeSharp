using Xunit;

namespace Geode.Client.Tests.Region.CachingProxyEntryLru;

/// <summary>
/// <see cref="IRegion.ContainsKeyOnServerAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxyEntryLru"/>.
/// </summary>
public class CachingProxyEntryLruContainsKeyOnServerAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // 同 CachingProxy:Put 寫 server + local map,ContainsKeyOnServerAsync
    // 走 wire 問 server。LRU 邊界對單 key 行為不變。
    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(CachingProxyEntryLruContainsKeyOnServerAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxyEntryLru)
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
        const string cacheName = nameof(CachingProxyEntryLruContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxyEntryLru)
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
