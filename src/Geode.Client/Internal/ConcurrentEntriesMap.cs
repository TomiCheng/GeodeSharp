using System.Collections.Concurrent;

namespace Geode.Client.Internal;

/// <summary>
/// Concurrent <see cref="EntriesMap"/> implementation. Mirrors cppcache
/// <c>ConcurrentEntriesMap</c>
/// (<c>cppcache/src/ConcurrentEntriesMap.hpp</c>).
/// </summary>
/// <remarks>
/// cppcache's <c>m_segments[]</c> + <c>MapSegment.m_map</c> + per-segment
/// spinlock / recursive_mutex / rehash machinery collapses onto a single
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> here — .NET's BCL
/// already does the striping (≈ <c>4 × Environment.ProcessorCount</c>
/// internal lock buckets) plus lock-free reads. The class itself stays
/// for the non-segment responsibilities (tombstone list, destroy tracker,
/// region back-ref, expiry manager) which arrive Phase 2+.
/// </remarks>
internal class ConcurrentEntriesMap(IServiceProvider serviceProvider, LocalRegion region)
    : EntriesMap
{
    IServiceProvider _ = serviceProvider;
    LocalRegion _0 = region;

    /// <summary>
    /// Backing store. Replaces cppcache <c>MapSegment[] m_segments</c>
    /// + per-segment <c>unordered_map</c> + manual lock striping.
    /// </summary>
    // TODO Phase 2+: ctor will take (concurrency, initialCapacity) from
    //   RegionAttributes once EntriesMapFactory's plain branch is wired.
    //   Default-construct for now so Count works against an empty map.
    private readonly ConcurrentDictionary<object, MapEntry> _map = new();

    /// <summary>
    /// Mirrors cppcache <c>ConcurrentEntriesMap::size()</c>
    /// (<c>cppcache/src/ConcurrentEntriesMap.cpp</c>) — sums per-segment
    /// counts. Here it's a direct read of the dictionary count.
    /// </summary>
    internal override int Count => _map.Count;
}
