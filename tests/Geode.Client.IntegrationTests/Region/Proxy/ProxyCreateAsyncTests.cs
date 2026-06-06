using Xunit;

namespace Geode.Client.IntegrationTests.Region.Proxy;

/// <summary>
/// Integration counterpart of the unit CreateAsync test for
/// <see cref="RegionShortcut.Proxy"/>. Proxy keeps no local map, so the
/// strict-create semantics live entirely on the server: the insert is
/// readable back via a wire Get, and a re-create on the same key surfaces the
/// server's EntryExistsException.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ProxyCreateAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task CreateAsync_NewKey_StoresValueOnServer()
    {
        const string cacheName = nameof(ProxyCreateAsyncTests) + "_create";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", ct);

            await region.CreateAsync("k", "v", ct: ct);

            // No local map in Proxy — Get round-trips to the server.
            Assert.Equal("v", await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    // cppcache parity: Proxy has no local entry map, and
    // ThinClientRegion::createNoThrow_remote sends a plain PUT (TcrMessagePut,
    // operation=null + flags=0, no ifNew) — so the server overwrites instead of
    // throwing. The strict EntryExistsException lives on the local FailIfPresent
    // check (ConcurrentEntriesMap.CreateAsync), which only runs in caching modes
    // (Local / CachingProxy / *EntryLru). On Proxy, create on an existing key
    // therefore degrades to a put-overwrite — no throw. (Differs from the Geode
    // Java client, which sends Operation.CREATE; we mirror cppcache.)
    public async Task CreateAsync_ExistingKey_OverwritesNoThrow()
    {
        const string cacheName = nameof(ProxyCreateAsyncTests) + "_exists";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", ct);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("test", ct);

            await region.CreateAsync("k", "v", ct: ct);

            // No local strict check on Proxy; the second create is a plain wire
            // put → overwrites the server value, no EntryExistsException.
            await region.CreateAsync("k", "v2", ct: ct);

            Assert.Equal("v2", await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
