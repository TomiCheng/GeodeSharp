using Xunit;

namespace Geode.Client.Tests.Eviction;

/// <summary>
/// Overflow-to-disk LRU eviction: a region with
/// <see cref="CacheDiskPolicy.Overflows"/> + an <see cref="IPersistenceManager"/>
/// spills evicted entry values to the persistence manager (leaving a token in
/// memory) instead of dropping them, and reads them back on a later get.
/// Exercises LRUOverFlowToDiskAction + GetFromDiskAsync + the region's
/// persistence-manager wiring (LocalRegion.InitializeAsync) end-to-end against a
/// test <see cref="InMemoryPersistenceManager"/>. The action + the user-supplied
/// IPersistenceManager path are implemented; only a built-in production
/// persistence manager (file / sqlite) is deferred to Phase 4.
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

    /// <summary>
    /// Overwriting a key whose value has already overflowed to disk: the LRU
    /// Put override (cppcache LRUEntriesMap::put, the <c>isOverflowed(oldValue)</c>
    /// branch) must read the stale on-disk value back and destroy the disk copy,
    /// then store the new value in memory. Verified via the test persistence
    /// manager's <c>DestroyCount</c>. (cppcache TODO at LRUEntriesMap.cpp:281-284
    /// flags exactly this path as needing a test.)
    /// </summary>
    [Fact]
    public async Task Put_OverOverflowedKey_DestroysStaleDiskCopy()
    {
        const string cacheName = nameof(OverflowEvictionTests) + "_reput";
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
            await region.PutAsync("k3", "v3", ct: ct);   // over limit → k1 spills to disk

            // Precondition: k1's value is on "disk", k1 still overflowed in memory
            // (no get on k1 yet — a get would read it back and clear the token).
            Assert.True(pm.Store.ContainsKey("k1"), "k1 should have spilled to disk");
            var destroysBefore = pm.DestroyCount;

            // Overwrite the still-overflowed key → the isOverflowed(oldValue) branch.
            await region.PutAsync("k1", "v1-new", ct: ct);

            // The stale on-disk copy of k1 was read back + destroyed (only this
            // branch calls DestroyAsync; eviction calls WriteAsync, not DestroyAsync).
            Assert.True(pm.DestroyCount > destroysBefore,
                "re-putting an overflowed key should destroy its stale disk copy");

            // The new value (in memory, MRU after the re-put) is what reads back.
            Assert.Equal("v1-new", await region.GetAsync("k1", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
