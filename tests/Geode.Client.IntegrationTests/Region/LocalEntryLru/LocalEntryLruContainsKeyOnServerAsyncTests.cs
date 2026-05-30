using Xunit;

namespace Geode.Client.IntegrationTests.Region.LocalEntryLru;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.LocalEntryLru.LocalEntryLruContainsKeyOnServerAsyncTests</c>.
/// Pool built against real cluster; LRU-bounded local region still must
/// throw <see cref="NotSupportedException"/> on
/// <c>ContainsKeyOnServerAsync</c>.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocalEntryLruContainsKeyOnServerAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_Throws()
    {
        const string cacheName = nameof(LocalEntryLruContainsKeyOnServerAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyOnServerAsync_NeverPut_Throws()
    {
        const string cacheName = nameof(LocalEntryLruContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
