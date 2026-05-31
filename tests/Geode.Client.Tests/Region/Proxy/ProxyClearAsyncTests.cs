using Xunit;

namespace Geode.Client.Tests.Region.Proxy;

/// <summary>
/// <see cref="IRegion.ClearAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Proxy"/> — pool-backed, no client-side
/// caching.
/// </summary>
/// <remarks>
/// <para>
/// Pool is registered to clear the <see cref="IRegionFactory"/> no-pool
/// guard; no actual server contact happens because the pre-cancelled
/// token must short-circuit <see cref="IRegion.ClearAsync"/> before it
/// touches the DM (which would otherwise fail NIE / connect-refused
/// against <c>localhost:40404</c>).
/// </para>
/// <para>
/// Drives the <c>ct.ThrowIfCancellationRequested()</c> entry guard on
/// <see cref="Internal.ThinClientRegion.ClearAsync"/> (mirrors cppcache
/// <c>ThinClientRegion::clear</c> at <c>ThinClientRegion.cpp:767</c>).
/// </para>
/// </remarks>
public class ProxyClearAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Proxy (CachingEnabled=false): no local map, so Clear is purely the
    // wire op — build ClearRegion, hand to the DM. With no server up the
    // wire surfaces NotConnectedException (cppcache GF_NOTCON); ServerOptional
    // treats that as "reached the server hand-off". Hard wire assertions
    // (REPLY vs EXCEPTION) live in Geode.Client.IntegrationTests.
    [Fact]
    public async Task ClearAsync_ReachesWire()
    {
        const string cacheName = nameof(ProxyClearAsyncTests) + "_wire";
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
        const string cacheName = nameof(ProxyClearAsyncTests);
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
