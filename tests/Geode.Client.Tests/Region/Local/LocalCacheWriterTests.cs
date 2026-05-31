using Xunit;

namespace Geode.Client.Tests.Region.Local;

/// <summary>
/// <see cref="ICacheWriter"/> veto dispatch on
/// <see cref="RegionShortcut.Local"/>: a writer runs <b>before</b> a
/// mutating op and can veto it (return <see langword="false"/> or throw) →
/// the op is abandoned with <see cref="CacheWriterException"/>. Local Put
/// drives <c>InvokeCacheWriterForEntryEvent</c> with no server needed.
/// </summary>
public class LocalCacheWriterTests(IGeodeCacheFactory factory)
{
    /// <summary>Counts before-event callbacks; configurable allow / throw.</summary>
    private sealed class RecordingWriter(bool allow = true, bool shouldThrow = false) : ICacheWriter
    {
        public int BeforeCreateCount;
        public int BeforeUpdateCount;

        public ValueTask<bool> BeforeCreateAsync(EntryEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref BeforeCreateCount);
            if (shouldThrow) throw new InvalidOperationException("writer boom");
            return new ValueTask<bool>(allow);
        }

        public ValueTask<bool> BeforeUpdateAsync(EntryEvent ev, CancellationToken ct = default)
        {
            Interlocked.Increment(ref BeforeUpdateCount);
            if (shouldThrow) throw new InvalidOperationException("writer boom");
            return new ValueTask<bool>(allow);
        }
    }

    // Writer allows (returns true) → Put proceeds; beforeCreate fired once.
    [Fact]
    public async Task Put_WriterAllows_Succeeds()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_allow";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var writer = new RecordingWriter(allow: true);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(writer)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, writer.BeforeCreateCount);
            Assert.True(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Writer vetoes (returns false) → Put throws CacheWriterException and the
    // entry is NOT written (veto runs before the local update).
    [Fact]
    public async Task Put_WriterVetoes_ThrowsAndDoesNotWrite()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_veto";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(new RecordingWriter(allow: false))
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<CacheWriterException>(
                () => region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken));

            Assert.False(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Writer throws → treated as veto → CacheWriterException (op abandoned).
    [Fact]
    public async Task Put_WriterThrows_VetoesAsCacheWriterException()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_throw";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(new RecordingWriter(shouldThrow: true))
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<CacheWriterException>(
                () => region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken));

            Assert.False(await region.ContainsKeyAsync("k", ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // Existing key → BEFORE_UPDATE event with non-null oldValue → beforeUpdate.
    [Fact]
    public async Task Put_ExistingKey_FiresBeforeUpdate()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_update";
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var writer = new RecordingWriter(allow: true);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(writer)
                .CreateAsync<string, string>("orders", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);
            await region.PutAsync("k", "v2", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, writer.BeforeCreateCount);
            Assert.Equal(1, writer.BeforeUpdateCount);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    // No writer → veto path skipped, Put succeeds.
    [Fact]
    public async Task Put_NoWriter_NoOp()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_nowriter";
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

    // Stats: one allowed Put records one region-level WriterCallTime sample.
    // (cppcache's writer path has no cache-level counter, unlike the listener.)
    // Isolated by a unique region name.
    [Fact]
    public async Task Put_RecordsWriterStats()
    {
        const string cacheName = nameof(LocalCacheWriterTests) + "_stats";
        using var regionCap = new MeterCapture(
            "Geode.Client.Region", "WriterCallTime", "regionName", "/writerstats");
        try
        {
            var cache = await factory.CreateAsync(cacheName, ct: default);
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .SetCacheWriter(new RecordingWriter(allow: true))
                .CreateAsync<string, string>("writerstats", TestContext.Current.CancellationToken);

            await region.PutAsync("k", "v", ct: TestContext.Current.CancellationToken);

            Assert.Equal(1, regionCap.Count);  // one WriterCallTime sample
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }
}
