using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.ContainsKeyAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/>.
/// </summary>
public class LocalEntryLruContainsKeyAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task ContainsKeyAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(ContainsKeyAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.ContainsKeyAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
