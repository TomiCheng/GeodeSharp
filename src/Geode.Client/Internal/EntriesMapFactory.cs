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
        EntriesMap? result;

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
            LRUAction.Action lruEvictionAction;
            var dpType = attributes.DiskPolicy;
            if (dpType == CacheDiskPolicy.Overflows)
            {
                lruEvictionAction = LRUAction.Action.OverflowToDisk;
            }
            else if ((dpType == CacheDiskPolicy.None) || prop.HeapLRULimitEnabled)
            {
                lruEvictionAction = LRUAction.Action.LocalDestroy;
                if (prop.HeapLRULimitEnabled) heapLRUEnabled = true;
            }
            else
            {
                // dpType 非 Overflows / 非 None + heapLRU 沒開(實務上 = Persist
                // disk policy)。cppcache 同樣回 nullptr(EntriesMapFactory.cpp:64)。
                // TODO Phase 4 disk-policy: caller 缺 null guard —
                //   LocalRegion lazy / CacheImpl.CreateRegionInternalAsync 直接把
                //   null 收進 _localEntriesMap,之後 InternalEntriesMap (`Value!`)
                //   會 NRE。cppcache 在 CacheImpl 端檢 nullptr → RegionCreationFailed。
                //   接 Persist / overflow-to-disk(LRUOverFlowToDiskAction 仍 NIE)時
                //   一併補 caching-enabled + null-map 的拒絕路徑。目前不可達。
                return null;
            }
            if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
            {
                var factory = LRUExpEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
                result = LRUEntriesMap.Create(serviceProvider,
                    factory, localRegion, lruEvictionAction, lruLimit, concurrencyChecksEnabled,
                    concurrency, heapLRUEnabled);
                //        result = new LRUEntriesMap(
                //            &expiryTaskmanager,
                //            std::make_unique<LRUExpEntryFactory>(concurrencyChecksEnabled),
                //            region, lruEvictionAction, lruLimit,
                //            concurrencyChecksEnabled, concurrency, heapLRUEnabled);
            }
            else
            {
                var factory = LRUEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
                result = LRUEntriesMap.Create(serviceProvider,
                    factory, localRegion, lruEvictionAction, lruLimit, concurrencyChecksEnabled,
                    concurrency, heapLRUEnabled);
                //        result = new LRUEntriesMap(
                //            &expiryTaskmanager,
                //            std::make_unique<LRUEntryFactory>(concurrencyChecksEnabled),
                //            region, lruEvictionAction, lruLimit,
                //            concurrencyChecksEnabled, concurrency, heapLRUEnabled);
            }
        }
        else if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
        {
            var factory = ExpEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
            result = ConcurrentEntriesMap.Create(serviceProvider,
                factory, concurrencyChecksEnabled, localRegion, concurrency);
            //    result = new ConcurrentEntriesMap(
            //        &expiryTaskmanager,
            //        std::make_unique<ExpEntryFactory>(concurrencyChecksEnabled),
            //        concurrencyChecksEnabled, region, concurrency);

            //    // ── 路徑 3:Plain Concurrent ────────────────────────────
        }
        else
        {
            var factory = EntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
            result = ConcurrentEntriesMap.Create(serviceProvider,
                factory, concurrencyChecksEnabled, localRegion, concurrency);
            //    result = new ConcurrentEntriesMap(
            //        &expiryTaskmanager,
            //        std::make_unique<EntryFactory>(concurrencyChecksEnabled),
            //        concurrencyChecksEnabled, region, concurrency);
        }

        result.Open(initialCapacity);     // ← 分配 segment 陣列、設容量
        return result;
    }
}
