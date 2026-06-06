using Xunit;

namespace Geode.Client.IntegrationTests.Region.LocalEntryLru;

/// <summary>
/// Integration counterpart of
/// <c>Geode.Client.Tests.Region.LocalEntryLru.LocalEntryLruCreateAsyncTests</c>.
/// Pool exists on the cache but the region is
/// <see cref="RegionShortcut.LocalEntryLru"/> — strict create stays pure-local;
/// the LRU bound doesn't change the semantics.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocalEntryLruCreateAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task CreateAsync_NewKey_StoresEntry()
    {
        const string cacheName = nameof(LocalEntryLruCreateAsyncTests) + "_create";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", ct);

            await region.CreateAsync("k", "v", ct: ct);

            Assert.Equal(1, region.LocalCount);
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
        const string cacheName = nameof(LocalEntryLruCreateAsyncTests) + "_exists";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", ct);

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
