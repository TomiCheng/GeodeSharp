using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxyEntryLru;

/// <summary>
/// Integration: <see cref="IRegion.ClearAsync"/> on a
/// <see cref="RegionShortcut.CachingProxyEntryLru"/> region clears BOTH the
/// server (wire <c>ClearRegion</c>) and the local map (<c>localClearNoThrow</c>
/// before the wire). Same contract as CachingProxy; the LRU bound doesn't change
/// clear (it's "remove everything", not eviction).
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyEntryLruClearAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ClearAsync_AfterPut_ClearsServerAndLocal()
    {
        const string cacheName = nameof(CachingProxyEntryLruClearAsyncTests) + "_clears";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxyEntryLru)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

            await region.PutAsync("k1", "v1", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k2", "v2", ct: TestContext.Current.CancellationToken);
            Assert.True(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.True(await region.ContainsKeyAsync("k1", ct: TestContext.Current.CancellationToken));

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            // Server cleared by the wire; local map cleared by localClearNoThrow.
            Assert.False(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.False(await region.ContainsKeyAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.False(await region.ContainsKeyAsync("k2", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
