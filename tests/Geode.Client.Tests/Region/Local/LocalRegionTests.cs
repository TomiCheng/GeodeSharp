using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// Behavioural tests for the <see cref="RegionShortcut.Local"/> family
/// (<c>Local</c> / <c>LocalEntryLru</c>) — region kinds with no server
/// backing and no pool requirement. Tests under this namespace exercise
/// the pure local-region surface (entry map, <c>LocalCount</c>, future
/// <c>LocalPut</c> / <c>LocalDestroy</c> / <c>LocalClear</c>).
/// </summary>
public class LocalRegionTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    [Theory]
    [InlineData(RegionShortcut.Local)]
    [InlineData(RegionShortcut.LocalEntryLru)]
    public async Task LocalCount_FreshRegion_IsZero(RegionShortcut shortcut)
    {
        var cacheName = $"{nameof(LocalCount_FreshRegion_IsZero)}_{shortcut}";
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(shortcut)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
