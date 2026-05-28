using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.UnregisterKeysAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalUnregisterKeysAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task UnregisterKeysAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(UnregisterKeysAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.UnregisterKeysAsync(new object[] { "k" }, ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
