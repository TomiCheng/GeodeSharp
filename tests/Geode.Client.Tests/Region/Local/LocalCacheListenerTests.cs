using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="ICacheListener"/> after-event dispatch on
/// <see cref="RegionShortcut.Local"/>: verifies the listener fires with the
/// right callback (afterCreate vs afterUpdate) and that the listener stats
/// (<c>CacheListenerCallsCompleted</c> + <c>ListenerCallTime</c>) are
/// recorded. Local Put drives <c>InvokeCacheListenerForEntryEvent</c> with
/// no server needed.
/// </summary>
public class LocalCacheListenerTests(IGeodeCacheFactory factory)
{
    /// <summary>Counts the after-event callbacks it receives.</summary>
    private sealed class RecordingListener : ICacheListener
    {
        public int AfterCreateCount;
        public int AfterUpdateCount;

        public ValueTask AfterCreateAsync(EntryEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AfterCreateCount);
            return default;
        }

        public ValueTask AfterUpdateAsync(EntryEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref AfterUpdateCount);
            return default;
        }
    }

    // Put a new key → AFTER_UPDATE event with null oldValue → afterCreate.
    [Fact]
    public async Task Put_NewKey_FiresAfterCreate()
    {
        const string cacheName = nameof(LocalCacheListenerTests) + "_create";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var listener = new RecordingListener();
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(listener)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.AfterCreateCount);
            Assert.Equal(0, listener.AfterUpdateCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Put an existing key → AFTER_UPDATE event with non-null oldValue →
    // afterUpdate (the second put), while the first stays afterCreate.
    [Fact]
    public async Task Put_ExistingKey_FiresAfterUpdate()
    {
        const string cacheName = nameof(LocalCacheListenerTests) + "_update";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var listener = new RecordingListener();
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(listener)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k", "v2", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, listener.AfterCreateCount);
            Assert.Equal(1, listener.AfterUpdateCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // No listener attached → dispatch is a clean no-op (Listener field stays
    // null), Put succeeds without firing anything.
    [Fact]
    public async Task Put_NoListener_NoOp()
    {
        const string cacheName = nameof(LocalCacheListenerTests) + "_nolistener";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.True(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Stats: one Put with a listener bumps the cache-level
    // CacheListenerCallsCompleted counter once and records one
    // region-level ListenerCallTime sample. Isolated from parallel tests by
    // a unique cacheName (set via configure) + a unique region name.
    [Fact]
    public async Task Put_RecordsListenerStats()
    {
        const string cacheName = nameof(LocalCacheListenerTests) + "_stats";
        const string regionPath = "/listenerstats";
        using var cacheCap = new MeterCapture(
            "Geode.Client.Cache", "CacheListenerCallsCompleted", "cacheName", cacheName);
        using var regionCap = new MeterCapture(
            "Geode.Client.Region", "ListenerCallTime", "regionName", regionPath);
        try
        {
            var cache = await factory.CreateAsync(
                cacheName, configure: (sysProps, _) => sysProps.Name = cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheListener(new RecordingListener())
                .CreateAsync<string, string>("listenerstats", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, cacheCap.Count);   // CacheListenerCallsCompleted +1
            Assert.Equal(1, regionCap.Count);  // one ListenerCallTime sample
            Assert.True(regionCap.Sum >= 0);   // elapsed seconds, non-negative
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
