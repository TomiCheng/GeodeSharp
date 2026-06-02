using Xunit;

namespace Geode.Client.Tests.Eviction;

/// <summary>
/// Overflow-to-disk LRU eviction: a region with
/// <see cref="CacheDiskPolicy.Overflows"/> + an <see cref="IPersistenceManager"/>
/// spills evicted entry values to the persistence manager (leaving a token in
/// memory) instead of dropping them, and reads them back on a later get.
/// Exercises LRUOverFlowToDiskAction + GetFromDiskAsync + the region's
/// persistence-manager wiring (LocalRegion.InitializeAsync) — all still NIE,
/// so this is a red light until that path lands.
/// </summary>
public class OverflowEvictionTests(IGeodeCacheFactory factory)
{
    [Fact]
    public async Task Put_BeyondLimit_OverflowsEvictedValueToDisk()
    {
        const string cacheName = nameof(OverflowEvictionTests);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: ct);
            var pm = new InMemoryPersistenceManager();

            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetLruEntriesLimit(2)
                .SetDiskPolicy(CacheDiskPolicy.Overflows)
                .SetPersistenceManager(pm)
                .CreateAsync<string, string>("orders", ct);

            await region.PutAsync("k1", "v1", ct: ct);
            await region.PutAsync("k2", "v2", ct: ct);
            await region.PutAsync("k3", "v3", ct: ct);   // over limit → evict LRU (k1) to disk

            // k1's value spilled to the persistence manager (not dropped).
            Assert.True(pm.WriteCount >= 1, "evicted value should have been written to the persistence manager");

            // a get on the evicted key reads it back from disk.
            Assert.Equal("v1", await region.GetAsync("k1", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
