using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.PutAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalPutAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalPutAsyncTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
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

    [Fact]
    public async Task PutAsync_WithCallback_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalPutAsyncTests) + "_callback";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.PutAsync("k", "v", callback: "cb", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Drives PutActions.CheckArgs (cppcache PutActions::checkArgs,
    // LocalRegion.cpp:1110-1117): value==null && delta==null → illegal
    // arg. user Put 永遠不帶 delta,所以 null value 直接命中。
    [Fact]
    public async Task PutAsync_NullValue_ThrowsArgumentException()
    {
        const string cacheName = nameof(LocalPutAsyncTests) + "_nullValue";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<ArgumentException>(
                () => region.PutAsync("k", null, ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Put-data smoke test on a caching-enabled Local region: storing an
    // entry should complete without throwing. Currently red — PutAsync
    // routes through UpdateNoThrowAsync's remote block (Normal flags are
    // neither Local nor Notification), so it hits the base
    // PutNoThrowRemoteAsync NIE before the local write runs. Flips green
    // once the local put path (PutNoThrowRemoteAsync no-op + PutLocalAsync
    // / EntriesMap) lands.
    [Fact]
    public async Task PutAsync_StoresValue()
    {
        const string cacheName = nameof(LocalPutAsyncTests) + "_put";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
