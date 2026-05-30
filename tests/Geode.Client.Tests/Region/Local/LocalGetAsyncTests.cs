using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.GetAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalGetAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Local region (caching enabled, no server): Put writes the local
    // entry map, Get reads it back. Mirrors cppcache LocalRegion::getNoThrow
    // local-hit path (no remote fetch).
    [Fact]
    public async Task GetAsync_AfterPut_ReturnsValue()
    {
        const string cacheName = nameof(LocalGetAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal("v", await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Never-put: local map miss → null (no server to fall back to).
    [Fact]
    public async Task GetAsync_NeverPut_ReturnsNull()
    {
        const string cacheName = nameof(LocalGetAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
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
        const string cacheName = nameof(LocalGetAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
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
