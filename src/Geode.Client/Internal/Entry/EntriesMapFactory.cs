using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal static class EntriesMapFactory
{
    public static EntriesMap? CreateMap(IServiceProvider serviceProvider, LocalRegion localRegion,
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
                return null;
            }
            if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
            {
                var factory = LruExpEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
                result = LruEntriesMap.Create(serviceProvider,
                    factory, localRegion, lruEvictionAction, lruLimit, concurrencyChecksEnabled,
                    concurrency, heapLRUEnabled);
            }
            else
            {
                var factory = LruEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
                result = LruEntriesMap.Create(serviceProvider,
                    factory, localRegion, lruEvictionAction, lruLimit, concurrencyChecksEnabled,
                    concurrency, heapLRUEnabled);
            }
        }
        else if (ttl > TimeSpan.Zero || idle > TimeSpan.Zero)
        {
            var factory = ExpEntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
            result = ConcurrentEntriesMap.Create(serviceProvider,
                factory, concurrencyChecksEnabled, localRegion, concurrency);
        }
        else
        {
            var factory = EntryFactory.Create(serviceProvider, concurrencyChecksEnabled);
            result = ConcurrentEntriesMap.Create(serviceProvider,
                factory, concurrencyChecksEnabled, localRegion, concurrency);
        }

        result?.Open(initialCapacity);
        return result;
    }
}
