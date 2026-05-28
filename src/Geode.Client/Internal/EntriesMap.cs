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

    /// <summary>
    /// Second-stage init invoked by <see cref="EntriesMapFactory.CreateMap"/>
    /// after construction. Mirrors cppcache <c>EntriesMap::open(initialCapacity)</c>
    /// (pure virtual) — in cppcache the body allocates the
    /// <c>MapSegment</c> array and initialises each segment. In the
    /// .NET port the storage (<see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>)
    /// is already live after ctor, so the default body is a no-op;
    /// subclasses override when they need capacity-aware allocation.
    /// </summary>
    internal virtual void Open(int initialCapacity) { }
}
