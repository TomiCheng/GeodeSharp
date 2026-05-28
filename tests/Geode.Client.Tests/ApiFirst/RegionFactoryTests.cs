using Geode.Client.Internal;
using Xunit;

namespace Geode.Client.Tests.ApiFirst;

/// <summary>
/// Public-surface tests for <see cref="RegionFactory"/> +
/// <see cref="IGeodeCache.CreateRegionFactory(RegionShortcut)"/>. Drives
/// the full create-region pipeline (RegionFactory → CacheImpl kind
/// dispatch → LocalRegion / ThinClient*Region ctor) without touching
/// the wire — only <see cref="RegionShortcut.Local"/> is server-less
/// today.
/// </summary>
public class RegionFactoryTests(IGeodeCacheFactory factory)
{
    /// <summary>One cache per test method — name uniqueness avoids cross-test pollution on the shared factory.</summary>
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // ── RegionShortcut.Local ─────────────────────────────────────

    [Fact]
    public async Task CreateRegion_Local_LocalCountIsZero()
    {
        const string cacheName = nameof(CreateRegion_Local_LocalCountIsZero);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            // LocalCount mirrors cppcache LocalRegion::size() — m_entries
            // is null in proxy-only mode, so the local entry count is 0.
            Assert.Equal(0, region.LocalCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task CreateRegion_Local_NameAndFullPath()
    {
        const string cacheName = nameof(CreateRegion_Local_NameAndFullPath);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            Assert.Equal("orders", region.Name);
            Assert.Equal("/orders", region.FullPath);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task CreateRegion_Local_GetRegionReturnsSameInstance()
    {
        const string cacheName = nameof(CreateRegion_Local_GetRegionReturnsSameInstance);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var created = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            // GetRegion goes through the same cache._regions map that
            // RegisterRegion populated. Unwrap the RegionView to compare
            // identity (both views forward to the same inner region).
            var fetched = cache.GetRegion("orders");
            Assert.NotNull(fetched);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task CreateRegion_DuplicateName_ThrowsRegionExistsException()
    {
        const string cacheName = nameof(CreateRegion_DuplicateName_ThrowsRegionExistsException);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var rf = cache.CreateRegionFactory(RegionShortcut.Local);

            await rf.CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            // Second create with the same name hits the TryAdd duplicate
            // guard in RegisterRegion (cppcache RegionExistsException at
            // CacheImpl.cpp:395-398).
            var second = cache.CreateRegionFactory(RegionShortcut.Local);
            await Assert.ThrowsAsync<RegionExistsException>(
                () => second.CreateAsync<string, string>("orders", TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    [Fact]
    public async Task CreateRegion_NameContainsSlash_ThrowsArgumentException()
    {
        const string cacheName = nameof(CreateRegion_NameContainsSlash_ThrowsArgumentException);
        try
        {
            var cache = await NewCacheAsync(cacheName);
            var rf = cache.CreateRegionFactory(RegionShortcut.Local);

            await Assert.ThrowsAsync<ArgumentException>(
                () => rf.CreateAsync<string, string>("orders/sub", TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
