using Xunit;

namespace Geode.Client.Tests.Region.CachingProxy;

/// <summary>
/// <see cref="IRegion.ContainsKeyOnServerAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxy"/>.
/// </summary>
public class CachingProxyContainsKeyOnServerAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // CachingProxy:Put 寫 server + local map。ContainsKeyOnServerAsync 一律
    // 走 wire(不查 local map),所以 server 有就 true。對映 cppcache
    // ThinClientRegion::containsKeyOnServer (ThinClientRegion.cpp:676) —
    // 跟 ContainsKeyAsync 走 local entry 的路徑完全分離。
    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(CachingProxyContainsKeyOnServerAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
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
        const string cacheName = nameof(CachingProxyContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
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
