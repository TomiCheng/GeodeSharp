using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests;

/// <summary>
/// Covers <see cref="RegionFactory.CreateAsync{TKey, TValue}(string, CancellationToken)"/>
/// orchestration (name validation, pool resolution, registration, typed
/// wrap). Mirrors cppcache <c>RegionFactory::create</c>
/// (<c>cppcache/src/RegionFactory.cpp:43-58</c>) + <c>CacheImpl::createRegion</c>
/// (<c>cppcache/src/CacheImpl.cpp:365-464</c>).
/// </summary>
public class RegionFactoryCreateAsyncTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    /// <summary>Build a cache with a single pool registered ("p"), enough to satisfy non-Local shortcuts.</summary>
    private static async Task<IGeodeCache> BuildCacheWithPoolAsync(
        ServiceProvider sp, CancellationToken ct, string poolName = "p")
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddServer("h", 40404)
            .BuildAsync(poolName, ct);
        return cache;
    }

    // ── Happy path ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsRegionWithExpectedNameAndFullPath()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);

        var region = await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<string, string>("orders", ct);

        Assert.Equal("orders", region.Name);
        Assert.Equal("/orders", region.FullPath);
    }

    [Fact]
    public async Task CreateAsync_RegistersRegionOnCache()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);

        await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<string, string>("orders", ct);

        Assert.NotNull(cache.GetRegion("orders"));
    }

    [Fact]
    public async Task CreateAsync_ReturnsTypedView_AssignableToBothIRegionVariants()
    {
        // Result must satisfy IRegion<TKey,TValue>; implementation wraps
        // a non-generic IRegion in RegionView so both views work.
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);

        var region = await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<int, string>("orders", ct);

        Assert.IsAssignableFrom<IRegion<int, string>>(region);
        Assert.IsAssignableFrom<IRegion>(region);
    }

    // ── Pool resolution ──────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NoExplicitPoolName_AutoFillsFromDefaultPool()
    {
        // cppcache RegionFactory.cpp:46-54 — non-LOCAL + empty PoolName
        // → resolve to default pool name; write back into attrs.
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct, poolName: "myDefaultPool");

        var region = await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<string, string>("r", ct);

        Assert.Equal("myDefaultPool", region.PoolName);
    }

    [Fact]
    public async Task CreateAsync_ExplicitPoolName_FlowsThrough()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct, poolName: "p");

        var region = await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .SetPoolName("p")
            .CreateAsync<string, string>("r", ct);

        Assert.Equal("p", region.PoolName);
    }

    // ── Name validation ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);
        var f = cache.CreateRegionFactory(RegionShortcut.Proxy);

        await Assert.ThrowsAsync<ArgumentException>(() => f.CreateAsync<string, string>("", ct));
    }

    [Fact]
    public async Task CreateAsync_NameWithSlash_ThrowsArgumentException()
    {
        // cppcache CacheImpl.cpp:385-388 — "Malformed name string,
        // contains region path seperator '/'".
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);
        var f = cache.CreateRegionFactory(RegionShortcut.Proxy);

        await Assert.ThrowsAsync<ArgumentException>(
            () => f.CreateAsync<string, string>("a/b", ct));
    }

    // ── Shortcut gating ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_LocalShortcut_ThrowsNotImplementedException()
    {
        // Local needs a server-less Region impl; Phase 2+.
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        var f = cache.CreateRegionFactory(RegionShortcut.Local);

        await Assert.ThrowsAsync<NotImplementedException>(
            () => f.CreateAsync<string, string>("r", ct));
    }

    // ── Pool unavailable ────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NoPoolRegistered_NonLocalShortcut_ThrowsInvalidOperationException()
    {
        // Non-Local shortcut + no PoolName + no DefaultPool → cannot resolve.
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        var f = cache.CreateRegionFactory(RegionShortcut.Proxy);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.CreateAsync<string, string>("r", ct));
    }

    [Fact]
    public async Task CreateAsync_UnknownExplicitPoolName_ThrowsInvalidOperationException()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct, poolName: "p");
        var f = cache.CreateRegionFactory(RegionShortcut.Proxy).SetPoolName("doesNotExist");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => f.CreateAsync<string, string>("r", ct));
    }

    // ── Duplicate registration ───────────────────────────────────

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsRegionExistsException()
    {
        // cppcache CacheImpl.cpp:395-398 — second create with same name
        // throws RegionExistsException.
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);

        await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<string, string>("orders", ct);

        await Assert.ThrowsAsync<RegionExistsException>(
            () => cache.CreateRegionFactory(RegionShortcut.Proxy)
                .CreateAsync<string, string>("orders", ct));
    }

    // ── Cache lifecycle ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_AfterCacheClose_ThrowsObjectDisposedException()
    {
        await using var sp = BuildSp();
        var ct = TestContext.Current.CancellationToken;
        var cache = await BuildCacheWithPoolAsync(sp, ct);
        var f = cache.CreateRegionFactory(RegionShortcut.Proxy);
        await cache.CloseAsync(ct);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => f.CreateAsync<string, string>("r", ct));
    }
}
