using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="IRegion.ContainsKeyOnServerAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Local"/>.
/// </summary>
public class LocalContainsKeyOnServerAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // Local region 沒有 server,ContainsKeyOnServerAsync 永遠拋
    // NotSupportedException — 即便剛 Put 過、local map 有 entry,也不可
    // fallback 到本地查。對映 LocalRegion.cs:978 / cppcache
    // LocalRegion::containsKeyOnServer 拋 UnsupportedOperationException 那條。
    [Fact]
    public async Task ContainsKeyOnServerAsync_AfterPut_Throws()
    {
        const string cacheName = nameof(LocalContainsKeyOnServerAsyncTests) + "_afterput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task ContainsKeyOnServerAsync_NeverPut_Throws()
    {
        const string cacheName = nameof(LocalContainsKeyOnServerAsyncTests) + "_neverput";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.ContainsKeyOnServerAsync("never-put", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
