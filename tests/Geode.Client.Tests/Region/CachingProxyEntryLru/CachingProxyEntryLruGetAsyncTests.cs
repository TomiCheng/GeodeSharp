using Xunit;

namespace Geode.Client.Tests.Region.CachingProxyEntryLru;

/// <summary>
/// <see cref="IRegion.GetAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxyEntryLru"/>.
/// </summary>
public class CachingProxyEntryLruGetAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // 同 CachingProxy:Put 寫 server + local map,Get 由 local map 命中。
    // LRU 邊界對單 key 行為不變。ServerOptional 包 wire leg。
    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(CachingProxyEntryLruGetAsyncTests) + "_afterput";
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

                Assert.Equal("v", await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task GetAsync_NeverPut_ReturnsNull()
    {
        const string cacheName = nameof(CachingProxyEntryLruGetAsyncTests) + "_neverput";
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
                Assert.Null(await region.GetAsync("never-put", ct: TestContext.Current.CancellationToken));
            });
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact(Skip = "Pending ct entry guard")]
    public async Task GetAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(CachingProxyEntryLruGetAsyncTests);
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

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.GetAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
