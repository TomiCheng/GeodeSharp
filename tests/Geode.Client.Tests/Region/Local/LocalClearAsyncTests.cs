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

    [Fact(Skip = "Pending ct entry guard")]
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
