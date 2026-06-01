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

    [Fact]
    public async Task PutAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalEntryLruPutAsyncTests);
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

    [Fact]
    public async Task PutAsync_WithCallback_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(LocalEntryLruPutAsyncTests) + "_callback";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.PutAsync("k", "v", callbackArgument: "cb", ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Put-data smoke test on an LRU-bounded Local region: storing an entry
    // should complete without throwing. RegionShortcut.LocalEntryLru routes
    // through LRUEntriesMap (extends ConcurrentEntriesMap); exercises the
    // same Put path plus whatever LRU bookkeeping the LRU map adds.
    [Fact]
    public async Task PutAsync_StoresValue()
    {
        const string cacheName = nameof(LocalEntryLruPutAsyncTests) + "_put";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task PutAsync_WithCallback_StoresValue()
    {
        const string cacheName = nameof(LocalEntryLruPutAsyncTests) + "_putCallback";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", callbackArgument: "cb", ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // LRU eviction: with a 2-entry limit, putting a 3rd entry must evict the
    // least-recently-used one, capping the local entry count at 2.
    // EntriesMap mutators 全面 async 化 + LRULocalDestroyAction.EvictAsync 接通後
    // (commit fad0859 / 後續 chain),整條 sync→async 的橋接已解開,Skip 拔掉。
    [Fact]
    public async Task PutAsync_BeyondLruLimit_EvictsToLimit()
    {
        const string cacheName = nameof(LocalEntryLruPutAsyncTests) + "_lruEvict";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.LocalEntryLru)
                .SetLruEntriesLimit(2)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k1", "v1", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k2", "v2", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k3", "v3", ct: TestContext.Current.CancellationToken);

            Assert.Equal(2, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
