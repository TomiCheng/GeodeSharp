using Xunit;

namespace Geode.Client.Tests.Region.CachingProxyEntryLru;

/// <summary>
/// <see cref="IRegion.ClearAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxyEntryLru"/> — pool-backed,
/// client-side caching with LRU eviction.
/// </summary>
/// <remarks>
/// Same cancellation contract as the other pool-backed variants
/// (<see cref="Proxy.ProxyClearAsyncTests"/>,
/// <see cref="CachingProxy.CachingProxyClearAsyncTests"/>); this shortcut
/// additionally exercises the <c>LRUEntriesMap</c> branch of
/// <see cref="Internal.EntriesMapFactory"/> (cppcache
/// <c>EntriesMapFactory.cpp:54-95</c>). The entry guard fires before
/// the map is consulted, so the LRU code path is not yet observable
/// here.
/// </remarks>
public class CachingProxyEntryLruClearAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task ClearAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(ClearAsync_AlreadyCancelledToken_Throws);
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
                () => region.ClearAsync(ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
