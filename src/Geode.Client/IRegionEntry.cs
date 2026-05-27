namespace Geode.Client;

/// <summary>
/// Snapshot of one cache entry. Mirrors cppcache <c>RegionEntry</c>
/// (<c>cppcache/include/geode/RegionEntry.hpp</c>).
/// </summary>
/// <remarks>
/// Empty marker today — members (<c>Key</c> / <c>Value</c> / <c>IsDestroyed</c>
/// / <c>RegionStatistics</c>) land when the local entry map / iteration
/// surface ships (Phase 2+).
/// </remarks>
public interface IRegionEntry
{
}
