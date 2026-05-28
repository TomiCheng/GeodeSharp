using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

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
internal class ConcurrentEntriesMap(IServiceProvider serviceProvider,
    EntryFactory factory, bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    : EntriesMap
{
    IServiceProvider _ = serviceProvider;
    LocalRegion _0 = region;

    static readonly ObjectFactory<ConcurrentEntriesMap> _objectFactory
        = ActivatorUtilities.CreateFactory<ConcurrentEntriesMap>(
            [typeof(EntryFactory), typeof(bool), typeof(LocalRegion), typeof(int)]);

    /// <summary>
    /// DI-aware factory. Same pattern as <see cref="LocalRegion.Create"/> —
    /// pre-baked <see cref="ObjectFactory{T}"/> avoids per-call ctor
    /// resolution.
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="concurrencyChecksEnabled"></param>
    /// <param name="concurrency"></param>
    internal static ConcurrentEntriesMap Create(IServiceProvider serviceProvider,
        EntryFactory factory, bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    {
        return _objectFactory(serviceProvider, [factory, concurrencyChecksEnabled, region, concurrency]);
    }

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
