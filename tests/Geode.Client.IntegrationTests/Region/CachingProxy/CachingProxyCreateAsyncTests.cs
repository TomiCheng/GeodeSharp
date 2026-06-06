using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxy;

/// <summary>
/// Integration counterpart of the unit CreateAsync test. Strict insert must
/// land the value end-to-end (server + local map), and a second create on the
/// same key must surface the server's EntryExistsException.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyCreateAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task CreateAsync_NewKey_StoresValue()
    {
        const string cacheName = nameof(CachingProxyCreateAsyncTests) + "_create";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", ct);

            await region.CreateAsync("k", "v", ct: ct);

            Assert.Equal("v", await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task CreateAsync_ExistingKey_ThrowsEntryExists()
    {
        const string cacheName = nameof(CachingProxyCreateAsyncTests) + "_exists";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", ct);

            await region.CreateAsync("k", "v", ct: ct);

            await Assert.ThrowsAsync<EntryExistsException>(
                () => region.CreateAsync("k", "v2", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
