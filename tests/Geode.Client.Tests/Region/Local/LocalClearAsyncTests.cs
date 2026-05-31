using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.ClearAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/> — pure local region, no pool.
/// </summary>
/// <remarks>
/// Drives the <c>ct.ThrowIfCancellationRequested()</c> entry guard
/// that cppcache <c>LocalRegion::clear</c> lacks (cppcache has no
/// CancellationToken concept). The pre-cancelled token must short-circuit
/// before touching the local entry map so a caller racing dispose
/// against Clear gets the standard .NET cancellation surface, not an
/// NRE / NIE from downstream state.
/// </remarks>
public class LocalClearAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Local region (caching enabled, no server): Put populates the local
    // entry map; Clear wipes it so a subsequent Get misses → null. Mirrors
    // cppcache LocalRegion::clear → localClearNoThrow → m_entries->clear()
    // (LocalRegion.cpp:2189-2226).
    [Fact]
    public async Task ClearAsync_AfterPut_RemovesLocalEntries()
    {
        const string cacheName = nameof(LocalClearAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
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
        const string cacheName = nameof(LocalClearAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
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
