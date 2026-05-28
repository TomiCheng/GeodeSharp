using Xunit;

namespace Geode.Client.Tests.Region;

/// <summary>
/// Public-surface tests for <see cref="IRegion"/> properties — kept
/// separate from <see cref="RegionFactoryTests"/> so the factory
/// pipeline and the per-region accessors regress independently.
/// Pool-backed shortcuts (<c>Proxy</c> / <c>CachingProxy</c> /
/// <c>CachingProxyEntryLru</c>) live here; the Local family lives
/// under <c>Region/Local</c>.
/// </summary>
public class RegionTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // ── IRegion.LocalCount (pool-backed shortcuts) ───────────────
    // Proxy: no local entry map, always 0.
    // CachingProxy / CachingProxyEntryLru: local map empty until a
    // server miss pulls an entry in. The pool is registered to pass
    // the RegionFactory no-pool guard; no actual server contact
    // happens (LocalCount is local-only).

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
