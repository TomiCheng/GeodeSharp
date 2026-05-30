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

    [Fact]
    public async Task DestroyAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalDestroyAsyncTests);
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

    // Put then destroy on a caching-enabled Local region: the stored entry
    // must be gone afterwards (LocalCount 1 -> 0). Red until the destroy path
    // lands — DestroyAsync today resolves to RegionInternal's NIE ("Strict
    // DestroyAsync is not yet wired"). Flips green once LocalRegion overrides
    // DestroyAsync -> DestroyNoThrowAsync -> UpdateNoThrowAsync<DestroyActions>,
    // mirroring the Put pipeline (PutAsync -> PutNoThrowAsync ->
    // UpdateNoThrowAsync<PutActions>).
    [Fact]
    public async Task DestroyAsync_AfterPut_RemovesEntry()
    {
        const string cacheName = nameof(LocalDestroyAsyncTests) + "_destroy";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
            Assert.Equal(1, region.LocalCount);

            await region.DestroyAsync("k", ct: TestContext.Current.CancellationToken);
            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // DestroyAsync on a missing key is lenient — mirrors cppcache
    // Region::destroy (NORMAL flag): the remote stage no-ops on a pure-local
    // region, sets afterRemote=true, and the local not-found is then tolerated
    // (cppcache MapSegment::removeWhenConcurrencyEnabled afterRemote branch
    // returns GF_NOERR). The strict-throw variant lives on cppcache
    // localDestroy() (LOCAL flag) — that's a separate API surface and is
    // not yet ported.
    [Fact]
    public async Task DestroyAsync_MissingKey_SilentlySucceeds()
    {
        const string cacheName = nameof(LocalDestroyAsyncTests) + "_destroy_missing";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            // No put — destroy on a key that was never inserted.
            await region.DestroyAsync("never-put", ct: TestContext.Current.CancellationToken);

            // Map stays empty; the lenient path is a no-op against missing keys.
            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
