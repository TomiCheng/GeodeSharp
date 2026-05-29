namespace Geode.Client;

/// <summary>
/// User-implemented hook receiving after-the-fact notifications of cache
/// events on a region. Mirrors cppcache <c>CacheListener</c>
/// (<c>cppcache/include/geode/CacheListener.hpp:40</c>).
/// </summary>
/// <remarks>
/// Every method carries a no-op default (C# default interface methods) so
/// implementers override only the events they care about — mirroring
/// cppcache's empty virtual bodies. Callbacks are <c>async</c>
/// (<see cref="ValueTask"/>): cppcache invokes the listener inline on the op's
/// dispatch thread, and the dispatcher awaits each callback, so an awaited
/// async callback keeps the same ordering / critical-path semantics while
/// letting notification I/O run without blocking a thread. The no-op default
/// is a completed <see cref="ValueTask"/> (<c>default</c>), so unhandled events
/// cost nothing. Do not fire-and-forget: dropping the returned task would
/// swallow exceptions and break event ordering.
/// </remarks>
public interface ICacheListener
{
    /// <summary>After a new entry is created. cppcache <c>afterCreate</c>.</summary>
    ValueTask AfterCreateAsync(EntryEvent ev, CancellationToken ct = default) => default;

    /// <summary>After an existing entry is updated. cppcache <c>afterUpdate</c>.</summary>
    ValueTask AfterUpdateAsync(EntryEvent ev, CancellationToken ct = default) => default;

    /// <summary>After an entry's value is invalidated. cppcache <c>afterInvalidate</c>.</summary>
    ValueTask AfterInvalidateAsync(EntryEvent ev, CancellationToken ct = default) => default;

    /// <summary>After an entry is destroyed / removed. cppcache <c>afterDestroy</c>.</summary>
    ValueTask AfterDestroyAsync(EntryEvent ev, CancellationToken ct = default) => default;

    /// <summary>After the region's entries are invalidated. cppcache <c>afterRegionInvalidate</c>.</summary>
    ValueTask AfterRegionInvalidateAsync(RegionEvent ev, CancellationToken ct = default) => default;

    /// <summary>After the region is destroyed. cppcache <c>afterRegionDestroy</c>.</summary>
    ValueTask AfterRegionDestroyAsync(RegionEvent ev, CancellationToken ct = default) => default;

    /// <summary>After the region is cleared. cppcache <c>afterRegionClear</c>.</summary>
    ValueTask AfterRegionClearAsync(RegionEvent ev, CancellationToken ct = default) => default;

    /// <summary>After a region becomes live (initial image complete). cppcache <c>afterRegionLive</c>.</summary>
    ValueTask AfterRegionLiveAsync(RegionEvent ev, CancellationToken ct = default) => default;

    /// <summary>When the listener is detached / the cache is closed. cppcache <c>close</c>.</summary>
    ValueTask CloseAsync(IRegion region, CancellationToken ct = default) => default;

    /// <summary>When all endpoints for the region are down. cppcache <c>afterRegionDisconnected</c>.</summary>
    ValueTask AfterRegionDisconnectedAsync(IRegion region, CancellationToken ct = default) => default;
}
