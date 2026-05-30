using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="ICacheLoader"/> read-through behaviour on
/// <see cref="IRegion.GetAsync"/> for <see cref="RegionShortcut.Local"/>.
/// </summary>
/// <remarks>
/// A cache loader is the last fallback in the get path: on a full miss
/// (local map + remote both empty), the loader produces the value, which
/// is then stored. Mirrors cppcache <c>LocalRegion::getNoThrow</c> loader
/// branch (<c>cppcache/src/LocalRegion.cpp:946-968</c>). Local shortcut has
/// no remote, so the miss goes straight to the loader — no server needed.
/// </remarks>
public class LocalCacheLoaderTests(IGeodeCacheFactory factory)
{
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    /// <summary>
    /// Deterministic loader: the value for a key is
    /// <c>key + callbackArgument</c>. Lets a test assert the loader both
    /// ran and received the key + callback the caller passed to
    /// <see cref="IRegion.GetAsync"/>.
    /// </summary>
    private sealed class SumLoader : ICacheLoader
    {
        public ValueTask<object?> LoadAsync(
            IRegion region, object key, object? callbackArgument, CancellationToken ct = default)
            => ValueTask.FromResult<object?>((int)key + (int)callbackArgument!);
    }

    // Get(5, callback: 3) misses the local map, invokes the loader with
    // key=5 + callbackArgument=3, and returns the computed 8.
    [Fact]
    public async Task GetAsync_Miss_InvokesLoader_ReturnsKeyPlusCallback()
    {
        const string cacheName = nameof(LocalCacheLoaderTests);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheLoader(new SumLoader())
                .CreateAsync<int, int>("orders", TestContext.Current.CancellationToken);

            var value = await region.GetAsync(5, callback: 3, ct: TestContext.Current.CancellationToken);

            Assert.Equal(8, value);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
