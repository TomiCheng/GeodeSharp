using Xunit;

namespace Geode.Client.Tests.Region.CachingProxy;

/// <summary>
/// <see cref="IRegion.UnregisterAllKeysAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxy"/>.
/// </summary>
public class CachingProxyUnregisterAllKeysAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task UnregisterAllKeysAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(CachingProxyUnregisterAllKeysAsyncTests);
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

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.UnregisterAllKeysAsync(ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
