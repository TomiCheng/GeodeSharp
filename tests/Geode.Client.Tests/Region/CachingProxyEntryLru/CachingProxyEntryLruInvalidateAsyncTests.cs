using Xunit;

namespace Geode.Client.Tests.Region.CachingProxyEntryLru;

/// <summary>
/// <see cref="IRegion.InvalidateAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxyEntryLru"/>.
/// </summary>
public class CachingProxyEntryLruInvalidateAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task InvalidateAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(InvalidateAsync_AlreadyCancelledToken_Throws);
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
                () => region.InvalidateAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
