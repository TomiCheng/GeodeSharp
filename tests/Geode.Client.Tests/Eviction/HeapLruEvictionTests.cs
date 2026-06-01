using System.Diagnostics;
using Xunit;

namespace Geode.Client.Tests.Eviction;

/// <summary>
/// End-to-end heap-LRU eviction. With a cache-wide
/// <see cref="SystemProperties.HeapLRULimit"/>, putting enough large
/// values to exceed the byte budget must trigger the background
/// <c>EvictionController</c> to evict entries from the caching-enabled
/// region. Exercises the full chain:
/// <para>
/// Put → LRUEntriesMap.UpdateMapSize → EvictionController.IncrementHeapSize
/// → SemaphoreSlim signal → SvcAsync → CheckHeapSizeAsync → EvictAsync
/// → LocalRegion.EvictAsync → LRUEntriesMap.ProcessLruAsync(int)
/// → LRULocalDestroyAction.EvictAsync → DestroyNoThrowAsync
/// → LRUEntriesMap.RemoveAsync (heap-size decrement closes the loop).
/// </para>
/// </summary>
public class HeapLruEvictionTests(IGeodeCacheFactory factory)
{
    [Fact]
    public async Task Put_BeyondHeapLimit_BackgroundControllerEvicts()
    {
        const string cacheName = nameof(HeapLruEvictionTests);
        var ct = TestContext.Current.CancellationToken;
        try
        {
            // 1 MB heap budget (HeapLRULimit is in MB, << 20 to bytes inside
            // EvictionController). StringDataConverter sizes a string at
            // Length*2 bytes, so a 20 000-char value ≈ 40 000 bytes — the
            // budget holds ~26 such entries. Many small-ish entries (not a few
            // huge ones) so the controller's (int)(percentage * count)
            // truncation doesn't round eviction down to zero.
            var cache = await factory.CreateAsync(
                cacheName,
                configure: (sp, _) => sp.HeapLRULimit = 1,   // MB
                ct);

            // RegionShortcut.Local is caching-enabled with no entry limit;
            // the cache-wide HeapLRULimit promotes it to a heap-LRU
            // LRUEntriesMap (EntriesMapFactory: heapLRUEnabled = true).
            var region = await cache
                .CreateRegionFactory(RegionShortcut.Local)
                .CreateAsync<string, string>("orders", ct);

            // 40 entries × ~40 000 bytes ≈ 1.6 MB worst-case heap (if the
            // background thread hasn't reacted yet when the last put lands).
            // 1.6 MB < 2× the 1 MB budget, so the controller's
            //   percentage = (heap - max)/max + delta
            // stays below 1.0 → entriesToEvict < count → it never evicts the
            // whole map in a single round. (Pushing past 2× over-budget makes
            // percentage > 1 and wipes everything; cppcache avoids that only
            // because its controller keeps heap near the limit in real time.)
            const int puts = 40;
            var big = new string('x', 20_000);              // ≈ 40 000 bytes
            for (var i = 0; i < puts; i++)
            {
                await region.PutAsync($"k{i}", $"{i}-{big}", ct: ct);
            }

            // Eviction is driven by the background SvcAsync loop, so the count
            // drops asynchronously after the puts return. Poll until it settles
            // below the insert count (or time out).
            var evicted = await WaitUntilAsync(
                () => region.LocalCount < puts,
                timeout: TimeSpan.FromSeconds(10));

            Assert.True(evicted,
                $"heap-LRU never evicted: LocalCount stayed at {region.LocalCount} of {puts}.");

            // Eviction happened (count < puts) but not everything was wiped —
            // the heap-size decrement on RemoveAsync converges the controller
            // once it drops under budget. Survivors land near the ~26-entry
            // budget; generous slack for the delta overshoot + put/evict race.
            Assert.InRange(region.LocalCount, 1, 35);
        }
        finally
        {
            await factory.DisposeCacheAsync(cacheName);
        }
    }

    /// <summary>
    /// Poll <paramref name="condition"/> until it returns <see langword="true"/>
    /// or <paramref name="timeout"/> elapses. Returns whether it became true.
    /// Used to observe the background eviction thread's effect without a fixed
    /// sleep.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(25);
        }
        return condition();
    }
}
