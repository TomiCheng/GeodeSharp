using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// Transaction guard tests for LOCAL regions. cppcache
/// <c>LocalRegion::updateNoThrow</c> (<c>LocalRegion.cpp:1650-1655</c>)
/// returns <c>GF_NOTSUP</c> when an ambient TXState exists and
/// <c>isLocalOp()</c> is true; the C# port mirrors that as
/// <see cref="NotSupportedException"/>.
/// </summary>
public class LocalRegionTransactionTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // cppcache LocalRegion::updateNoThrow (LocalRegion.cpp:1650-1655):
    //   TAction action(*this);
    //   TXState* txState = action.m_txState;
    //   if (txState != nullptr) {
    //     if (isLocalOp(&eventFlags)) {
    //       return GF_NOTSUP;        // ← 本 test 要打到的路徑
    //     }
    //     ...
    //   }
    // LOCAL / LOCAL_ENTRY_LRU 走純 LocalRegion class,isLocalOp() 為 true →
    // 在 ambient tx 中 PutAsync 應拋 NotSupportedException。
    [Theory]
    [InlineData(RegionShortcut.Local)]
    [InlineData(RegionShortcut.LocalEntryLru)]
    public async Task PutAsync_InsideTransaction_OnLocalRegion_ThrowsNotSupported(RegionShortcut shortcut)
    {
        var cacheName = $"{nameof(LocalRegionTransactionTests)}_{shortcut}";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(shortcut)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            cache.TransactionManager.Begin();

            await Assert.ThrowsAsync<NotSupportedException>(
                () => region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
