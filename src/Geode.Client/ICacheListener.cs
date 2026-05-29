namespace Geode.Client;

/// <summary>
/// User-implemented hook receiving after-the-fact notifications of cache
/// events on a region. Mirrors cppcache <c>CacheListener</c>
/// (<c>cppcache/include/geode/CacheListener.hpp:40</c>).
/// </summary>
/// <remarks>
/// Every method carries a no-op default (C# default interface methods) so
/// implementers override only the events they care about — mirroring
/// cppcache's empty virtual bodies. Callbacks are synchronous
/// (<c>void</c>), matching the cppcache / clicache <c>CacheListener</c>
/// shape; the async-first rule governs the client's own I/O APIs, not the
/// user callback contract.
/// </remarks>
public interface ICacheListener
{
    /// <summary>After a new entry is created. cppcache <c>afterCreate</c>.</summary>
    void AfterCreate(EntryEvent ev) { }

    /// <summary>After an existing entry is updated. cppcache <c>afterUpdate</c>.</summary>
    void AfterUpdate(EntryEvent ev) { }

    /// <summary>After an entry's value is invalidated. cppcache <c>afterInvalidate</c>.</summary>
    void AfterInvalidate(EntryEvent ev) { }

    /// <summary>After an entry is destroyed / removed. cppcache <c>afterDestroy</c>.</summary>
    void AfterDestroy(EntryEvent ev) { }

    /// <summary>After the region's entries are invalidated. cppcache <c>afterRegionInvalidate</c>.</summary>
    void AfterRegionInvalidate(RegionEvent ev) { }

    /// <summary>After the region is destroyed. cppcache <c>afterRegionDestroy</c>.</summary>
    void AfterRegionDestroy(RegionEvent ev) { }

    /// <summary>After the region is cleared. cppcache <c>afterRegionClear</c>.</summary>
    void AfterRegionClear(RegionEvent ev) { }

    /// <summary>After a region becomes live (initial image complete). cppcache <c>afterRegionLive</c>.</summary>
    void AfterRegionLive(RegionEvent ev) { }

    /// <summary>When the listener is detached / the cache is closed. cppcache <c>close</c>.</summary>
    void Close(IRegion region) { }

    /// <summary>When all endpoints for the region are down. cppcache <c>afterRegionDisconnected</c>.</summary>
    void AfterRegionDisconnected(IRegion region) { }
}
