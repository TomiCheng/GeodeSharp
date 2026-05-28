using Xunit;

namespace Geode.Client.Tests.Region.Proxy;

/// <summary>
/// <see cref="IRegion.RegisterKeysAsync"/> behavioural tests for
/// <see cref="RegionShortcut.Proxy"/>.
/// </summary>
public class ProxyRegisterKeysAsyncTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact(Skip = "Pending ct entry guard")]
    public async Task RegisterKeysAsync_AlreadyCancelledToken_Throws()
    {
        const string cacheName = nameof(RegisterKeysAsync_AlreadyCancelledToken_Throws);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer("localhost", 40404)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => region.RegisterKeysAsync(new object[] { "k" }, ct: cts.Token));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
