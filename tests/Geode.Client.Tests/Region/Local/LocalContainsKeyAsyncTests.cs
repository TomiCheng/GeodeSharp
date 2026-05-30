using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.ContainsKeyAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalContainsKeyAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Local region with caching enabled: Put then ContainsKeyAsync should
    // return true (key landed in the local entry map; no server involved).
    // Mirrors cppcache LocalRegion::containsKey_internal (LocalRegion.cpp:817):
    //   CachingEnabled=true → delegate to m_entries->containsKey → segment lookup.
    [Fact]
    public async Task ContainsKeyAsync_AfterPut_ReturnsTrue()
    {
        const string cacheName = nameof(LocalContainsKeyAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.True(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Never-put: ContainsKeyAsync 直接回 false,沒走 server。
    [Fact]
    public async Task ContainsKeyAsync_NeverPut_ReturnsFalse()
    {
        const string cacheName = nameof(LocalContainsKeyAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.False(await region.ContainsKeyAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact(Skip = "Pending ct entry guard")]
    public async Task ContainsKeyAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalContainsKeyAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.ContainsKeyOnServerAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
