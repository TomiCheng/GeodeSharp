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

    [Fact(Skip = "Pending ct entry guard")]
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
