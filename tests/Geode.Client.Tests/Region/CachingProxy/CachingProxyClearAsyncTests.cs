using Xunit;

namespace Geode.Client.Tests.Region.CachingProxy;

/// <summary>
/// <see cref="IRegion.ClearAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxy"/> — pool-backed, client-side
/// caching enabled.
/// </summary>
/// <remarks>
/// Same setup as the <c>Proxy</c> variant; the difference is that the
/// caching path will eventually need to drop local entries too
/// (cppcache <c>ThinClientRegion::clear</c> calls
/// <c>localClearNoThrow</c> before the wire op). The cancellation guard
/// still fires before any of that runs.
/// </remarks>
public class CachingProxyClearAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // CachingProxy: cppcache ThinClientRegion::clear clears the local map
    // (localClearNoThrow) BEFORE the wire op, then sends ClearRegion. With
    // no server up the wire surfaces NotConnectedException; ServerOptional
    // soft-passes. (The local-clear-before-wire step is not yet ported —
    // current ClearAsync only does the wire leg; see PORTING.md.)
    [Fact]
    public async Task ClearAsync_ReachesWire()
    {
        const string cacheName = nameof(CachingProxyClearAsyncTests) + "_wire";
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

            await ServerOptional.RunAsync(
                () => region.ClearAsync(ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ClearAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(CachingProxyClearAsyncTests);
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
                () => region.ClearAsync(ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
