using Xunit;

namespace Geode.Client.IntegrationTests.Region.Partition;

/// <summary>
/// Integration: <see cref="IRegion.ClearAsync"/> against a server-side
/// <b>PARTITION</b> region ("parttest"). Server-side clear of a partitioned
/// region is version-dependent: unsupported before GEODE-7670
/// (<c>PartitionedRegion.clear()</c> / <c>basicClear</c> throw
/// <c>UnsupportedOperationException</c>), supported from ~1.14 onwards.
/// <para>
/// Empirically determined (this probe): the pinned <c>apachegeode/geode:latest</c>
/// image does <b>NOT</b> support it — the server throws
/// <c>UnsupportedOperationException</c> → EXCEPTION reply →
/// <see cref="GeodeException"/> client-side, and the data is left intact (the
/// server rejects before touching it). Contrast the REPLICATE "test" region,
/// which clears successfully (see <c>CachingProxyClearAsyncTests</c>). If the
/// base image is ever bumped past 1.14 this test flips and should be updated.
/// </para>
/// <para>
/// Uses a <see cref="RegionShortcut.Proxy"/> client (no local cache) so the
/// assertion is purely about the server's response, not the local map. This is
/// the only end-to-end coverage of <c>ClearAsync</c>'s EXCEPTION-reply branch.
/// </para>
/// </summary>
[Collection(nameof(GeodeCollection))]
public class PartitionClearAsyncTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Fact]
    public async Task ClearAsync_PartitionRegion_ServerRejectsAsUnsupported()
    {
        const string cacheName = nameof(PartitionClearAsyncTests) + "_unsupported";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            await cache.PoolManager.CreateFactory()
                .AddServer(fx.LocatorHost, fx.ServerPort)
                .BuildAsync("pool", TestContext.Current.CancellationToken);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Proxy)
                .SetPoolName("pool")
                .CreateAsync<string, string>("parttest", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
            Assert.True(await region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));

            // PartitionedRegion.basicClear throws UnsupportedOperationException →
            // server EXCEPTION reply → GeodeException (the EXCEPTION branch of
            // ThinClientRegion.ClearAsync).
            var ex = await Assert.ThrowsAsync<GeodeException>(
                () => region.ClearAsync(ct: TestContext.Current.CancellationToken));
            Assert.Contains("UnsupportedOperationException", ex.Message, StringComparison.Ordinal);

            // Server rejected before touching data → the entry survives.
            Assert.True(await region.ContainsKeyOnServerAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
