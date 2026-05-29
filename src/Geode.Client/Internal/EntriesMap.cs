using Geode.Client.Protocol;

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
    /// <summary>
    /// Reads the <see cref="MapEntry"/> and its current value for
    /// <paramref name="key"/> (both <see langword="null"/> when absent).
    /// Mirrors cppcache <c>EntriesMap::getEntry</c>
    /// (<c>cppcache/src/EntriesMap.hpp:90-92</c>, pure virtual) — the two
    /// <c>shared_ptr&amp;</c> out-params become a returned tuple per the
    /// codebase out-param → tuple convention.
    /// </summary>
    public (MapEntry? Entry, object? Value) GetEntry(object key) => throw new NotImplementedException();

    /// <summary>
    /// Inserts <paramref name="newValue"/> for <paramref name="key"/>
    /// only when the key is absent; returns the freshly-created
    /// <see cref="MapEntry"/> handle and the prior value (the latter is
    /// <see langword="null"/> on success, populated when the key already
    /// existed). Mirrors cppcache <c>EntriesMap::create</c>
    /// (<c>cppcache/src/EntriesMap.hpp:73-78</c>, pure virtual) — the two
    /// <c>shared_ptr&amp;</c> out-params (<c>me</c>, <c>oldValue</c>)
    /// become a returned tuple per the codebase out-param → tuple
    /// convention. cppcache <c>GfErrType</c> collapses to throws
    /// (<see cref="EntryExistsException"/> for the present-key case).
    /// </summary>
    public (MapEntry? Entry, object? OldValue) Create(
        object key,
        object newValue,
        int updateCount,
        int destroyTracker,
        VersionTag? versionTag) => throw new NotImplementedException();
    public int AddTrackerForEntry(object key, object? oldValue, bool addIfAbsent, bool failIfPresent, bool value) => throw new NotImplementedException();
    public void RemoveTrackerForEntry(object key) => throw new NotImplementedException();


    public abstract (MapEntry Entry, object? OldValue, bool IsUpdate) Put(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null);

    /// <summary>
    /// 把 overflow 到磁碟的 entry value 撈回記憶體。Mirrors cppcache
    /// <c>EntriesMap::getFromDisk</c>
    /// (<c>cppcache/src/EntriesMap.hpp:175</c>, pure virtual)。
    /// cppcache <c>MapSegment::getFromDisc</c>(typo,'Disc')純 forward 到這,
    /// MapSegment collapse 後直接 call 本 method。回 <see langword="null"/>
    /// 表 persistence manager 撈不回(已被 GC 或檔案損毀),caller 應視為
    /// <c>InvalidDelta</c> / get-miss。
    /// </summary>
    public abstract object? GetFromDisk(object key, MapEntry entry);
}
