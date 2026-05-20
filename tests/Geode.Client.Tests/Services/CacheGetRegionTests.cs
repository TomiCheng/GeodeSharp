using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

/// <summary>
/// Lookup-path unit tests for <see cref="Cache.GetRegion(string)"/> and
/// the typed overload. Covers what does NOT need <c>_regions</c> to be
/// populated — path validation, leading-slash strip, sub-region NIE,
/// post-close ObjectDisposedException, typed null pass-through. Tests
/// that need a populated registry (real region lookup, RegionView
/// wrapping) wait until a fake <see cref="IRegion"/> infrastructure
/// lands.
/// </summary>
public class CacheGetRegionTests
{
    /// <summary>
    /// Build a minimal in-memory <see cref="Cache"/> for lookup-path
    /// tests. Skips DI scope wiring + pool init — we only exercise
    /// <see cref="Cache.GetRegion(string)"/>'s validation / miss
    /// branches, none of which touch the pool or the network.
    /// </summary>
    private static Cache NewCache()
    {
        var scope = new CacheScopeContext();
        scope.Initialize(string.Empty, new GeodeClientOptions());
        var sp = new ServiceCollection().BuildServiceProvider();
        var poolMgr = new PoolManager();
        var tccm = new TcrConnectionManager(
            scope, NullLogger<TcrConnectionManager>.Instance, sp);
        var adapter = new TypedResultAdapter();
        var typeRegistry = new TypeRegistry(NullLogger<TypeRegistry>.Instance);
        return new Cache(sp, scope, poolMgr, tccm, adapter, typeRegistry);
    }

    // ── Path validation (cppcache CacheImpl.cpp:488-490) ────────

    [Fact]
    public void GetRegion_null_path_throws_ArgumentNullException()
    {
        var cache = NewCache();
        Assert.Throws<ArgumentNullException>(() => cache.GetRegion(null!));
    }

    [Fact]
    public void GetRegion_empty_path_throws_ArgumentException()
    {
        var cache = NewCache();
        Assert.Throws<ArgumentException>(() => cache.GetRegion(""));
    }

    [Fact]
    public void GetRegion_slash_only_path_throws_ArgumentException()
    {
        var cache = NewCache();
        Assert.Throws<ArgumentException>(() => cache.GetRegion("/"));
    }

    // ── Missing region returns null (cppcache CacheImpl.cpp:502) ──

    [Fact]
    public void GetRegion_unknown_name_returns_null()
    {
        var cache = NewCache();
        Assert.Null(cache.GetRegion("missing"));
    }

    [Fact]
    public void GetRegion_unknown_name_with_leading_slash_returns_null()
    {
        // Mirrors cppcache CacheImpl.cpp:495-497 — leading "/" is stripped
        // before the first-segment lookup; result is the same as no slash.
        var cache = NewCache();
        Assert.Null(cache.GetRegion("/missing"));
    }

    // ── Sub-region path with missing parent returns null ────────
    // cppcache CacheImpl.cpp:502-506 — sub-region recursion only fires
    // when findRegion(stepname) found the parent. With an empty
    // registry the lookup short-circuits to null *before* the NIE
    // sub-region guard. Hitting the NIE requires a populated parent;
    // that test waits until we have a fake-IRegion infrastructure.

    [Fact]
    public void GetRegion_sub_region_path_with_missing_parent_returns_null()
    {
        var cache = NewCache();
        Assert.Null(cache.GetRegion("parent/child"));
    }

    [Fact]
    public void GetRegion_sub_region_path_with_leading_slash_and_missing_parent_returns_null()
    {
        var cache = NewCache();
        Assert.Null(cache.GetRegion("/parent/child"));
    }

    // ── Lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task GetRegion_after_CloseAsync_throws_ObjectDisposedException()
    {
        var cache = NewCache();
        await cache.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => cache.GetRegion("anything"));
    }

    // ── Typed overload pass-through ──────────────────────────────

    [Fact]
    public void GetRegion_typed_returns_null_when_underlying_returns_null()
    {
        var cache = NewCache();
        Assert.Null(cache.GetRegion<string, byte[]>("missing"));
    }

    [Fact]
    public void GetRegion_typed_sub_region_with_missing_parent_returns_null()
    {
        // Typed overload delegates to untyped GetRegion(path); when
        // parent is absent the untyped lookup returns null, so the
        // typed wrapper also returns null (no RegionView allocation).
        var cache = NewCache();
        Assert.Null(cache.GetRegion<string, byte[]>("parent/child"));
    }

    [Fact]
    public async Task GetRegion_typed_after_CloseAsync_throws_ObjectDisposedException()
    {
        var cache = NewCache();
        await cache.CloseAsync(TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(
            () => cache.GetRegion<string, byte[]>("anything"));
    }
}
