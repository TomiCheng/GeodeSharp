using Xunit;

namespace Geode.Client.IntegrationTests.Region.Proxy;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.Proxy.ProxyContainsKeyAsyncTests</c>:
/// same behaviour tests, but the pool talks to the real Geode cluster
/// from <see cref="GeodeFixture"/> instead of a (likely-absent)
/// <c>localhost:40404</c>, so wire-going ops actually complete. No
/// <c>ServerOptional</c> wrap — <c>NotConnectedException</c> here is a
/// real failure.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ProxyContainsKeyAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Proxy region (CachingEnabled=false): Put lands on the real server,
    // but the client-side ContainsKeyAsync is pure-local. Mirrors cppcache
    // LocalRegion::containsKey_internal (LocalRegion.cpp:822) early-out —
    // CachingEnabled=false → return false. Server-side state is moot here.
    [Fact]
    public async Task ContainsKeyAsync_AfterPut_StillReturnsFalse()
    {
        const string cacheName = nameof(ProxyContainsKeyAsyncTests) + "_afterput";
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

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.False(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyAsync_NeverPut_ReturnsFalse()
    {
        const string cacheName = nameof(ProxyContainsKeyAsyncTests) + "_neverput";
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

            Assert.False(await region.ContainsKeyAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
