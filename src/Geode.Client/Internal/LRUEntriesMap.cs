namespace Geode.Client.Internal;

/// <summary>
/// LRU-bounded <see cref="ConcurrentEntriesMap"/>: tracks per-entry LRU
/// order and evicts (or overflows to disk) once <c>lruLimit</c> is hit.
/// Mirrors cppcache <c>LRUEntriesMap</c>
/// (<c>cppcache/src/LRUEntriesMap.hpp:45</c>). Skeleton only — members
/// land with the LRU subsystem (Phase 2+: <c>LRUQueue</c>, <c>LRUAction</c>
/// strategy, eviction triggers).
/// </summary>
internal sealed class LRUEntriesMap(IServiceProvider serviceProvider, LocalRegion region)
    : ConcurrentEntriesMap(serviceProvider, region)
{
}
