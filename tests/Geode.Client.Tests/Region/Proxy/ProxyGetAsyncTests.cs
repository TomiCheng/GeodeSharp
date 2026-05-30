using Xunit;

namespace Geode.Client.Tests.Region.Proxy;

/// <summary>
/// <see cref="IRegion.GetAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Proxy"/>.
/// </summary>
public class ProxyGetAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Proxy (CachingEnabled=false): no local map, so Get always goes to
    // the server. Put lands on the server; Get fetches it back over the
    // wire. ServerOptional: NotConnectedException is a soft pass when no
    // server is up (see Geode.Client.IntegrationTests for the hard assert).
    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(ProxyGetAsyncTests) + "_afterput";
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
        const string cacheName = nameof(ProxyGetAsyncTests) + "_neverput";
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
        const string cacheName = nameof(ProxyGetAsyncTests);
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
                () => region.GetAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
