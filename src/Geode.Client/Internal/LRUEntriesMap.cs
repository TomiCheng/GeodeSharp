using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// LRU-bounded <see cref="ConcurrentEntriesMap"/>: tracks per-entry LRU
/// order and evicts (or overflows to disk) once <c>lruLimit</c> is hit.
/// Mirrors cppcache <c>LRUEntriesMap</c>
/// (<c>cppcache/src/LRUEntriesMap.hpp:45</c>). Skeleton only — members
/// land with the LRU subsystem (Phase 2+: <c>LRUQueue</c>, <c>LRUAction</c>
/// strategy, eviction triggers).
/// </summary>
internal sealed class LRUEntriesMap(IServiceProvider serviceProvider,
    EntryFactory factory, LocalRegion region, LRUAction.Action lruEvictionAction,
    int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
    : ConcurrentEntriesMap(serviceProvider, factory, concurrencyChecksEnabled, region, concurrency)
{
    static readonly ObjectFactory<LRUEntriesMap> _objectFactory
        = ActivatorUtilities.CreateFactory<LRUEntriesMap>(
            [typeof(EntryFactory), typeof(LocalRegion), typeof(LRUAction.Action), typeof(int),
                typeof(bool), typeof(int), typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Same pattern as <see cref="LocalRegion.Create"/> —
    /// pre-baked <see cref="ObjectFactory{T}"/> avoids per-call ctor
    /// resolution. <c>new</c> hides the inherited
    /// <see cref="ConcurrentEntriesMap.Create"/>: the bases are independent
    /// static factories (no virtual dispatch on static methods), each builds
    /// its own concrete type.
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="lruEvictionAction"></param>
    /// <param name="lruLimit"></param>
    /// <param name="concurrencyChecksEnabled"></param>
    /// <param name="concurrency"></param>
    /// <param name="heapLRUEnabled"></param>
    internal static LRUEntriesMap Create(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LRUAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
    {
        return _objectFactory(serviceProvider, [factory, region, lruEvictionAction, lruLimit,
            concurrencyChecksEnabled, concurrency, heapLRUEnabled]);
    }
}
