using Xunit;

namespace Geode.Client.IntegrationTests.Region.CachingProxy;

/// <summary>
/// Integration counterpart of the unit InvalidateAsync test. Put populates
/// server + local map; Invalidate must clear the value end-to-end so a later
/// Get (which round-trips on the local INVALID-token miss) returns null.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CachingProxyInvalidateAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task InvalidateAsync_AfterPut_ClearsValue()
    {
        const string cacheName = nameof(CachingProxyInvalidateAsyncTests) + "_invalidate";
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

            await region.PutAsync("k", "v", ct: ct);
            Assert.Equal("v", await region.GetAsync("k", ct: ct));

            await region.InvalidateAsync("k", ct: ct);

            // Invalidate clears the value end-to-end: the local INVALID token misses,
            // and the server round-trip finds it invalidated there too → null.
            Assert.Null(await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
