using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.CreateAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalCreateAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task CreateAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalCreateAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.CreateAsync("k", "v", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Strict insert on a fresh key: the entry must land in the local map
    // (LocalCount 0 -> 1) and be readable back. Red until CreateAsync is
    // wired — RegionInternal.CreateAsync today is NIE ("Strict CreateAsync
    // is not yet wired"). Flips green once LocalRegion overrides CreateAsync
    // -> CreateNoThrowAsync -> UpdateNoThrowAsync<CreateActions>, mirroring
    // the Put pipeline (PutActions) with FailIfPresent = true.
    [Fact]
    public async Task CreateAsync_NewKey_StoresEntry()
    {
        const string cacheName = nameof(LocalCreateAsyncTests) + "_create";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", ct);

            await region.CreateAsync("k", "v", ct: ct);

            Assert.Equal(1, region.LocalCount);
            Assert.Equal("v", await region.GetAsync("k", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Strict semantics: a second create on an existing key must throw
    // EntryExistsException — this is what distinguishes Create from Put
    // (cppcache CreateActions::s_failIfPresent = true, LocalRegion.cpp:1188;
    // the failIfPresent guard in UpdateNoThrow returns GF_CACHE_ENTRY_EXISTS).
    [Fact]
    public async Task CreateAsync_ExistingKey_ThrowsEntryExists()
    {
        const string cacheName = nameof(LocalCreateAsyncTests) + "_exists";
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", ct);

            await region.CreateAsync("k", "v", ct: ct);

            await Assert.ThrowsAsync<EntryExistsException>(
                () => region.CreateAsync("k", "v2", ct: ct));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
