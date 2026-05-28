using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.DestroyAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalDestroyAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task DestroyAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(DestroyAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.DestroyAsync("k", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
