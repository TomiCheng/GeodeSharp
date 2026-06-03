using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.InvalidateAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/>.
/// </summary>
public class LocalEntryLruInvalidateAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task InvalidateAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalEntryLruInvalidateAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.InvalidateAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task InvalidateAsync_AfterPut_KeepsKeyClearsValue()
    {
        const string cacheName = nameof(LocalEntryLruInvalidateAsyncTests) + "_invalidate";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", ct);

            await region.PutAsync("k", "v", ct: ct);
            Assert.Equal(1, region.LocalCount);

            await region.InvalidateAsync("k", ct: ct);

            // Key stays (unlike destroy → 0) — the entry is still counted.
            Assert.Equal(1, region.LocalCount);
            // Value cleared: get sees the INVALID token, not a hit → null.
            Assert.Null(await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
