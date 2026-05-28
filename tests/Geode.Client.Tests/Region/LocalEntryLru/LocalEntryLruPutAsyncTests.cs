using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.PutAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/>.
/// </summary>
public class LocalEntryLruPutAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(PutAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.PutAsync("k", "v", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
