using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests;

/// <summary>
/// Locks the cppcache <c>RegionFactory::setRegionShortcut()</c>
/// (<c>cppcache/src/RegionFactory.cpp:60-80</c>) preset matrix: ctor
/// applies <c>cachingEnabled</c> + <c>lruEntriesLimit</c> presets per
/// <see cref="RegionShortcut"/>, then user setter calls override.
/// </summary>
public class RegionFactoryShortcutPresetTests
{
    // cppcache CacheImpl.hpp:43
    private const int DefaultLruMaximumEntries = 100000;

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static async Task<RegionFactory> BuildFactoryAsync(
        ServiceProvider sp, CancellationToken ct, RegionShortcut shortcut)
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        return cache.CreateRegionFactory(shortcut);
    }

    // ── Ctor-time presets per shortcut ────────────────────────────

    [Fact]
    public async Task Proxy_DisablesCaching()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.Proxy);
        var attrs = f.SnapshotAttributes();

        Assert.False(attrs.CachingEnabled);
        Assert.Equal(0, attrs.LruEntriesLimit);
    }

    [Fact]
    public async Task CachingProxy_EnablesCaching_NoLru()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.CachingProxy);
        var attrs = f.SnapshotAttributes();

        Assert.True(attrs.CachingEnabled);
        Assert.Equal(0, attrs.LruEntriesLimit);
    }

    [Fact]
    public async Task CachingProxyEntryLru_EnablesCachingAndSetsLru()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.CachingProxyEntryLru);
        var attrs = f.SnapshotAttributes();

        Assert.True(attrs.CachingEnabled);
        Assert.Equal(DefaultLruMaximumEntries, attrs.LruEntriesLimit);
    }

    [Fact]
    public async Task Local_NoPresetsApplied()
    {
        // cppcache RegionFactory.cpp:73 — empty case; caching=true (the
        // RegionAttributes default) and LRU disabled both carry through.
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.Local);
        var attrs = f.SnapshotAttributes();

        Assert.True(attrs.CachingEnabled);
        Assert.Equal(0, attrs.LruEntriesLimit);
    }

    [Fact]
    public async Task LocalEntryLru_SetsLru_KeepsCachingDefault()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.LocalEntryLru);
        var attrs = f.SnapshotAttributes();

        Assert.True(attrs.CachingEnabled);
        Assert.Equal(DefaultLruMaximumEntries, attrs.LruEntriesLimit);
    }

    // ── User setter overrides ctor preset ────────────────────────

    [Fact]
    public async Task UserSetCachingEnabled_OverridesProxyPreset()
    {
        // Precedence test: ctor wrote caching=false for Proxy; user
        // setter must be able to override (cppcache: setters mutate
        // the same RegionAttributesFactory after the ctor preset, so
        // last write wins).
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.Proxy);

        f.SetCachingEnabled(true);

        Assert.True(f.SnapshotAttributes().CachingEnabled);
    }

    [Fact]
    public async Task UserSetLruEntriesLimit_OverridesEntryLruPreset()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.CachingProxyEntryLru);

        f.SetLruEntriesLimit(42);

        Assert.Equal(42, f.SnapshotAttributes().LruEntriesLimit);
    }

    // ── Shortcut accessor ────────────────────────────────────────

    [Fact]
    public async Task Shortcut_ReflectsCtorArgument()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken, RegionShortcut.LocalEntryLru);

        Assert.Equal(RegionShortcut.LocalEntryLru, f.Shortcut);
    }
}
