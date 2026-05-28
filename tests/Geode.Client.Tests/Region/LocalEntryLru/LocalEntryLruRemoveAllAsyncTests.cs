using Xunit;

namespace Geode.Client.Tests.Region.LocalEntryLru;

/// <summary>
/// <see cref="IRegion.RemoveAllAsync"/> behavioural tests for
/// <see cref="RegionShortcut.LocalEntryLru"/>.
/// </summary>
public class LocalEntryLruRemoveAllAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task RemoveAllAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(RemoveAllAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.RemoveAllAsync(new[] { "k" }, ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
