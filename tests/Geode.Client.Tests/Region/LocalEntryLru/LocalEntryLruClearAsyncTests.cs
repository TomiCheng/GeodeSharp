using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.ClearAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/> — pure local region
/// with LRU eviction, no pool.
/// </summary>
/// <remarks>
/// Same cancellation contract as
/// <see cref="Local.LocalClearAsyncTests"/>; the LRU variant exercises
/// the <c>LRUEntriesMap</c> path of <see cref="EntriesMapFactory"/>
/// (cppcache <c>EntriesMapFactory.cpp:54-95</c>), but the entry guard
/// fires before the map is consulted so the LRU code path is not yet
/// observable here — same expected surface as the non-LRU sibling.
/// </remarks>
public class LocalEntryLruClearAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Same as Local: clear wipes the whole local map regardless of the LRU
    // bound (clear is "remove everything", not eviction). Mirrors cppcache
    // LocalRegion::clear → m_entries->clear() (LocalRegion.cpp:2189-2226).
    [Fact]
    public async Task ClearAsync_AfterPut_RemovesLocalEntries()
    {
        const string cacheName = nameof(LocalEntryLruClearAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k1", "v1", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k2", "v2", ct: TestContext.Current.CancellationToken);
            Assert.Equal("v1", await region.GetAsync("k1", ct: TestContext.Current.CancellationToken));

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            Assert.Null(await region.GetAsync("k1", ct: TestContext.Current.CancellationToken));
            Assert.Null(await region.GetAsync("k2", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ClearAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalEntryLruClearAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.ClearAsync(ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
