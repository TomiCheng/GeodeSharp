namespace Geode.Client.Internal;

/// <summary>
/// Abstract concurrent map of <see cref="MapEntry"/> backing a
/// caching-enabled region. Mirrors cppcache <c>EntriesMap</c>
/// (<c>cppcache/src/EntriesMap.hpp</c>). Skeleton only — most members
/// land with the caching-enabled phase (Phase 2+); concrete impl is
/// <see cref="ConcurrentEntriesMap"/>.
/// </summary>
internal abstract class EntriesMap
{
    /// <summary>
    /// Number of entries currently held. Mirrors cppcache
    /// <c>EntriesMap::size()</c> (<c>cppcache/src/EntriesMap.hpp:133</c>,
    /// pure virtual).
    /// </summary>
    internal abstract int Count { get; }
}
