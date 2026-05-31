using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxy;

/// <summary>
/// Integration: <see cref="IRegion.ClearAsync"/> on a
/// <see cref="RegionShortcut.CachingProxy"/> region clears BOTH the server
/// (wire <c>ClearRegion</c>) AND the local map (<c>localClearNoThrow</c> runs
/// before the wire). The local-clear leg is the stale-read fix — a unit test
/// can't observe it (no server, so Put never populates the local map), so it
/// is verified here against the Testcontainers cluster (REPLICATE region
/// "test", which supports server-side clear).
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyClearAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ClearAsync_AfterPut_ClearsServerAndLocal()
    {
        const string cacheName = nameof(CachingProxyClearAsyncTests) + "_clears";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.CachingProxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", TestContext.Current.CancellationToken);

            await region.PutAsync("k1", "v1", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k2", "v2", ct: TestContext.Current.CancellationToken);

            // Sanity: present on BOTH the server (wire) and the local map before clear.
            Assert.True(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.True(await region.ContainsKeyAsync("k1", ct: TestContext.Current.CancellationToken));

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            // Server side cleared by the wire ClearRegion (REPLICATE supports clear).
            Assert.False(await region.ContainsKeyOnServerAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.False(await region.ContainsKeyOnServerAsync("k2", ct: TestContext.Current.CancellationToken));

            // Local map cleared by localClearNoThrow BEFORE the wire — the stale-read
            // fix. Before the fix ThinClientRegion.ClearAsync only sent the wire, so
            // ContainsKeyAsync (local containment) would still return true here.
            Assert.False(await region.ContainsKeyAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.False(await region.ContainsKeyAsync("k2", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
