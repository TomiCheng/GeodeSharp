using Xunit;

namespace Geode.Client.Tests.Region.CachingProxy;

/// <summary>
/// <see cref="IRegion.GetAsync"/> behavioural tests for
/// <see cref="RegionShortcut.CachingProxy"/>.
/// </summary>
public class CachingProxyGetAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // CachingProxy: Put writes server + local map. Get should serve from
    // the local map (cppcache get local-hit short-circuit) and return "v".
    // ServerOptional wraps the Put leg (wire) so unit soft-passes without
    // a server; hard wire assertion lives in Geode.Client.IntegrationTests.
    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(CachingProxyGetAsyncTests) + "_afterput";
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
        const string cacheName = nameof(CachingProxyGetAsyncTests) + "_neverput";
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
        const string cacheName = nameof(CachingProxyGetAsyncTests);
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
                () => region.GetAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
