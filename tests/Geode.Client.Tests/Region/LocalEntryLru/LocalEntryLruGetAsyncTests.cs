using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.GetAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/>.
/// </summary>
public class LocalEntryLruGetAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Same as Local: caching enabled, no server. Put → Get round-trips
    // through the local entry map. LRU bound doesn't change single-key
    // behaviour.
    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(LocalEntryLruGetAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal("v", await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task GetAsync_NeverPut_ReturnsNull()
    {
        const string cacheName = nameof(LocalEntryLruGetAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Null(await region.GetAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact(Skip = "Pending ct entry guard")]
    public async Task GetAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalEntryLruGetAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.GetAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
