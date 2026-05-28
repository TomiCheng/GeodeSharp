using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geode.Client.Internal;

/// <summary>
/// Picks the concrete <see cref="EntriesMap"/> implementation (plain
/// <see cref="ConcurrentEntriesMap"/> vs. LRU vs. expiry-aware variants)
/// based on <see cref="RegionAttributes"/>. Mirrors cppcache
/// <c>EntriesMapFactory::createMap</c>
/// (<c>cppcache/src/EntriesMapFactory.cpp:38-100</c>).
/// </summary>
internal static class EntriesMapFactory
{
    /// <summary>
    /// Build the <see cref="EntriesMap"/> for a caching-enabled region.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache decision tree (<c>EntriesMapFactory.cpp:54-97</c>):
    /// <list type="bullet">
    ///   <item>LRU enabled (<c>lruLimit &gt; 0</c> or process heap-LRU)
    ///         → <c>LRUEntriesMap</c> + DiskPolicy / LRUAction wiring</item>
    ///   <item>TTL or idle &gt; 0 → <c>ConcurrentEntriesMap</c> + <c>ExpEntryFactory</c></item>
    ///   <item>otherwise → plain <c>ConcurrentEntriesMap</c> + <c>EntryFactory</c></item>
    /// </list>
    /// Phase 1.x only the third branch is real; LRU and entry-level
    /// expiry land with their respective subsystems (<c>LRUEntriesMap</c>,
    /// <c>ExpiryTaskManager</c>, <c>DiskPolicy</c>, heap-LRU
    /// <c>SystemProperties</c>).
    /// </para>
    /// </remarks>
    public static EntriesMap? CreateMap(
        IServiceProvider serviceProvider,
        LocalRegion localRegion,
        RegionAttributes attributes)
    {
        EntriesMap? result = null;

        var initialCapacity = attributes.InitialCapacity;
        var concurrency = attributes.ConcurrencyLevel;
        var lruLimit = attributes.LruEntriesLimit;
        var ttl = attributes.EntryTimeToLive;
        var idle = attributes.EntryIdleTimeout;
        var concurrencyChecksEnabled = attributes.ConcurrencyChecksEnabled;
        var heapLRUEnabled = false;

        
        var prop = serviceProvider.GetRequiredService<SystemProperties>();
        // var expiryTaskmanager = serviceProvider.GetRequiredService<ExpiryTaskManager>(); TODO

        //// ── 路徑 1:LRU map ──────────────────────────────────────
        if ((lruLimit != 0) || prop.HeapLRULimitEnabled)
        {
            //  LRUAction::Action lruEvictionAction;
            var dpType = attributes.DiskPolicy;
            if (dpType == CacheDiskPolicy.Overflows)
            {
                //        lruEvictionAction = LRUAction::OVERFLOW_TO_DISK;
            }
            else if ((dpType == CacheDiskPolicy.None) || prop.HeapLRULimitEnabled)
            {
                //        lruEvictionAction = LRUAction::LOCAL_DESTROY;
                if (prop.HeapLRULimitEnabled) heapLRUEnabled = true;
            }
            else
            {
                return null;
            }
            if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
            {
                //        result = new LRUEntriesMap(
                //            &expiryTaskmanager,
                //            std::make_unique<LRUExpEntryFactory>(concurrencyChecksEnabled),
                //            region, lruEvictionAction, lruLimit,
                //            concurrencyChecksEnabled, concurrency, heapLRUEnabled);
            }
            else
            {
                _ = heapLRUEnabled;
                //        result = new LRUEntriesMap(
                //            &expiryTaskmanager,
                //            std::make_unique<LRUEntryFactory>(concurrencyChecksEnabled),
                //            region, lruEvictionAction, lruLimit,
                //            concurrencyChecksEnabled, concurrency, heapLRUEnabled);
            }


        }
        else if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
        {
            result = ActivatorUtilities.CreateInstance<ConcurrentEntriesMap>(serviceProvider, localRegion);
            //    result = new ConcurrentEntriesMap(
            //        &expiryTaskmanager,
            //        std::make_unique<ExpEntryFactory>(concurrencyChecksEnabled),
            //        concurrencyChecksEnabled, region, concurrency);

            //    // ── 路徑 3:Plain Concurrent ────────────────────────────
        }
        else
        {
            result = ActivatorUtilities.CreateInstance<ConcurrentEntriesMap>(serviceProvider, localRegion);
            //    result = new ConcurrentEntriesMap(
            //        &expiryTaskmanager,
            //        std::make_unique<EntryFactory>(concurrencyChecksEnabled),
            //        concurrencyChecksEnabled, region, concurrency);
        }

        //result->open(initialCapacity);     // ← 分配 segment 陣列、設容量
        return result;
    }
}
