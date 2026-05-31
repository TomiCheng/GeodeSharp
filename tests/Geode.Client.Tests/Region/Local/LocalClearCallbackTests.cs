using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// Region-event <see cref="ICacheWriter"/> / <see cref="ICacheListener"/>
/// dispatch on <see cref="RegionShortcut.Local"/> <see cref="IRegion.ClearAsync"/>:
/// the writer's <c>BeforeRegionClear</c> veto point and the listener's
/// <c>AfterRegionClear</c> notification, plus the region-level
/// <c>WriterCallTime</c> / <c>ListenerCallTime</c> stats. Local clear drives the
/// worker (<c>LocalClearNoThrowAsync</c> →
/// <c>InvokeCacheWriter/ListenerForRegionEventAsync</c>) with no server needed.
/// </summary>
public class LocalClearCallbackTests(IGeodeCacheFactory factory)
{
    /// <summary>Records region-clear vetoes; configurable allow / throw.</summary>
    private sealed class RecordingClearWriter(bool allow = true, bool shouldThrow = false) : ICacheWriter
    {
        public int BeforeRegionClearCount;

        public ValueTask<bool> BeforeRegionClearAsync(RegionEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref BeforeRegionClearCount);
            if (shouldThrow) throw new InvalidOperationException("writer boom");
            return new ValueTask<bool>(allow);
        }
    }

    /// <summary>Counts afterRegionClear notifications; optional throw.</summary>
    private sealed class RecordingClearListener(bool shouldThrow = false) : ICacheListener
    {
        public int AfterRegionClearCount;

        public ValueTask AfterRegionClearAsync(RegionEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AfterRegionClearCount);
            if (shouldThrow) throw new InvalidOperationException("listener boom");
            return default;
        }
    }

    // Writer vetoes the clear (BeforeRegionClear → false) → ClearAsync throws
    // CacheWriterException and the entries are NOT removed (veto runs before the
    // map wipe in the worker).
    [Fact]
    public async Task ClearAsync_WriterVetoes_ThrowsAndDoesNotClear()
    {
        const string cacheName = nameof(LocalClearCallbackTests) + "_veto";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(new RecordingClearWriter(allow: false))
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<CacheWriterException>(
                () => region.ClearAsync(ct: TestContext.Current.CancellationToken));

            // veto runs before InternalEntriesMap.Clear() → entry survives.
            Assert.Equal("v", await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Writer allows (BeforeRegionClear → true) → clear proceeds and records one
    // region-level WriterCallTime sample. No Put: the only writer call is the
    // clear's beforeRegionClear (a Put would add an entry-event writer sample).
    [Fact]
    public async Task ClearAsync_WriterAllows_RecordsWriterStats()
    {
        const string cacheName = nameof(LocalClearCallbackTests) + "_writerstats";
        using var regionCap = new MeterCapture(
            "Geode.Client.Region", "WriterCallTime", "regionName", "/clearwriterstats");
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var writer = new RecordingClearWriter(allow: true);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(writer)
                .CreateAsync<string, string>("clearwriterstats", TestContext.Current.CancellationToken);

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, writer.BeforeRegionClearCount);
            Assert.Equal(1, regionCap.Count);  // one WriterCallTime sample
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Listener fires afterRegionClear once and the entries are gone (the listener
    // runs AFTER the map wipe on the LOCAL path: eventFlags=LOCAL → !isNormal()).
    [Fact]
    public async Task ClearAsync_FiresAfterRegionClearListener()
    {
        const string cacheName = nameof(LocalClearCallbackTests) + "_listener";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var listener = new RecordingClearListener();
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(listener)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.AfterRegionClearCount);
            Assert.Null(await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Stats: clear with a listener records one region-level ListenerCallTime
    // sample but does NOT bump the cache-level CacheListenerCallsCompleted —
    // cppcache's AFTER_REGION_CLEAR case omits incListenerCalls (unlike
    // afterRegionInvalidate / afterRegionDestroy). Locks in that asymmetry.
    [Fact]
    public async Task ClearAsync_RecordsListenerStats_ButNotCacheCounter()
    {
        const string cacheName = nameof(LocalClearCallbackTests) + "_listenerstats";
        using var cacheCap = new MeterCapture(
            "Geode.Client.Cache", "CacheListenerCallsCompleted", "cacheName", cacheName);
        using var regionCap = new MeterCapture(
            "Geode.Client.Region", "ListenerCallTime", "regionName", "/clearlistenerstats");
        try
        {
            var cache = await factory.CreateAsync(
                cacheName, configure: (sysProps, _) => sysProps.Name = cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(new RecordingClearListener())
                .CreateAsync<string, string>("clearlistenerstats", TestContext.Current.CancellationToken);

            await region.ClearAsync(ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, regionCap.Count);  // one ListenerCallTime sample
            Assert.Equal(0, cacheCap.Count);   // AFTER_REGION_CLEAR does NOT bump the cache counter
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Listener throws → wrapped in CacheListenerException (mirrors the entry-event
    // listener). The map is still wiped (the listener runs after the clear), but
    // the exception surfaces to the caller.
    [Fact]
    public async Task ClearAsync_ListenerThrows_ThrowsCacheListenerException()
    {
        const string cacheName = nameof(LocalClearCallbackTests) + "_listenerthrow";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(new RecordingClearListener(shouldThrow: true))
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<CacheListenerException>(
                () => region.ClearAsync(ct: TestContext.Current.CancellationToken));

            // map wiped before the listener ran → entry gone despite the throw.
            Assert.Null(await region.GetAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
