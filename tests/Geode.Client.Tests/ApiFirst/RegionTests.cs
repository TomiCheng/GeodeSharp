using Xunit;

namespace Geode.Client.Tests.ApiFirst;

/// <summary>
/// Public-surface tests for <see cref="IRegion"/> properties — kept separate
/// from <see cref="RegionFactoryTests"/> so the factory pipeline and the
/// per-region accessors regress independently.
/// </summary>
public class RegionTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // ── IRegion.LocalCount ───────────────────────────────────────
    // Fresh region across every RegionShortcut → LocalCount == 0.
    //   Proxy: no local entry map, always 0.
    //   CachingProxy / CachingProxyEntryLru: local map empty until a
    //     server miss pulls an entry in.
    //   Local / LocalEntryLru: local map empty until LocalPut.
    // Split into two methods by pool requirement:
    //   * Local family — no pool, runs against the bare factory.
    //   * Proxy / CachingProxy family — needs a pool registered (no
    //     server actually contacted; Size is local-only).

    [Theory]
    [InlineData(RegionShortcut.Local)]
    [InlineData(RegionShortcut.LocalEntryLru)]
    public async Task LocalCount_FreshRegion_IsZero_NoPool(RegionShortcut shortcut)
    {
        var cacheName = $"{nameof(LocalCount_FreshRegion_IsZero_NoPool)}_{shortcut}";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(shortcut)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Theory]
    [InlineData(RegionShortcut.Proxy)]
    [InlineData(RegionShortcut.CachingProxy)]
    [InlineData(RegionShortcut.CachingProxyEntryLru)]
    public async Task LocalCount_FreshRegion_IsZero_NeedsPool(RegionShortcut shortcut)
    {
        var cacheName = $"{nameof(LocalCount_FreshRegion_IsZero_NeedsPool)}_{shortcut}";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var pool = await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(shortcut)
                .SetPoolName("pool")
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
