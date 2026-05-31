using Xunit;

namespace Geode.Client.IntegrationTests.Region.Proxy;

/// <summary>
/// Integration: <see cref="IRegion.ClearAsync"/> on a
/// <see cref="RegionShortcut.Proxy"/> region clears the server-side REPLICATE
/// region "test" via the wire <c>ClearRegion</c>. Proxy has no local cache, so
/// there is nothing to clear client-side — this is the pure server-clear case.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ProxyClearAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ClearAsync_AfterPut_ClearsServer()
    {
        const string cacheName = nameof(ProxyClearAsyncTests) + "_clears";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

            await region.PutAsync("k1", "v1", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k2", "v2", ct: TestContext.Current.CancellationToken);
            Assert.True(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            Assert.False(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.False(await region.ContainsKeyOnServerAsync("k2", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
