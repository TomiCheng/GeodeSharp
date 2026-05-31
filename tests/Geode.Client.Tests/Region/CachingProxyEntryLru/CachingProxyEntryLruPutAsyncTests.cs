using Xunit;

namespace Geode.Client.Tests.Region.CachingProxyEntryLru;

/// <summary>
/// <see cref="IRegion.PutAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxyEntryLru"/>.
/// </summary>
public class CachingProxyEntryLruPutAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(CachingProxyEntryLruPutAsyncTests);
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
                () => region.PutAsync("k", "v", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task PutAsync_WithCallback_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(CachingProxyEntryLruPutAsyncTests) + "_callback";
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
                () => region.PutAsync("k", "v", callbackArgument: "cb", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
