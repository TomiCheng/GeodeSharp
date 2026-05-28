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

    // ── IRegion.Size ─────────────────────────────────────────────
    // Fresh region across every RegionShortcut → Size == 0.
    //   Proxy: no local entry map, always 0.
    //   CachingProxy / CachingProxyEntryLru: local map empty until a
    //     server miss pulls an entry in.
    //   Local / LocalEntryLru: local map empty until LocalPut.
    // Today only Local works end-to-end; the proxy variants will RED
    // on CreateAsync ("No pool for non-local region.") until Phase 2+
    // wires the wire path. Keeping the cases in place as walking
    // skeleton — they turn green as each path lands.

    [Theory]
    [InlineData(RegionShortcut.Local)]
    [InlineData(RegionShortcut.LocalEntryLru)]
    [InlineData(RegionShortcut.Proxy)]
    [InlineData(RegionShortcut.CachingProxy)]
    [InlineData(RegionShortcut.CachingProxyEntryLru)]
    public async Task Size_FreshRegion_IsZero(RegionShortcut shortcut)
    {
        var cacheName = $"{nameof(Size_FreshRegion_IsZero)}_{shortcut}";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(shortcut)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Equal(0, region.Size);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
