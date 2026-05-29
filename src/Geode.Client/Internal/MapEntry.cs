namespace Geode.Client.Internal;

/// <summary>
/// Per-key entry held inside <see cref="EntriesMap"/>: value plus
/// version / tracker / tombstone metadata. Mirrors cppcache pure-abstract
/// <c>MapEntry</c> (<c>cppcache/src/MapEntry.hpp:48</c>) — concrete impls
/// are <see cref="MapEntryImpl"/> (non-versioned) /
/// <see cref="VersionedMapEntryImpl"/>. <c>abstract class</c> (not
/// interface) to match the sibling <see cref="EntriesMap"/> and because
/// <see cref="VersionedMapEntryImpl"/> reuses <see cref="MapEntryImpl"/>'s
/// value storage (implementation inheritance). Skeleton only — members
/// land with the caching-enabled phase (Phase 2+).
/// </summary>
internal abstract class MapEntry
{
    /// <summary>
    /// Whether this entry already has an entry-expiry task scheduled.
    /// Mirrors cppcache
    /// <c>MapEntryImpl::getExpProperties().task_scheduled()</c>
    /// (<c>cppcache/src/MapEntry.hpp</c>, <c>ExpProperties</c> chain).
    /// Flat property here for Phase 2+ NIE convenience; cppcache's
    /// two-step <c>ExpProperties</c> accessor will be reintroduced when
    /// the full expiry surface lands.
    /// </summary>
    public virtual bool IsExpiryTaskScheduled => throw new NotImplementedException(
        "MapEntry.IsExpiryTaskScheduled: pending Phase 2+ expiry plumbing.");

    /// <summary>
    /// Per-entry concurrency-check version stamp. Mirrors cppcache
    /// <c>MapEntryImpl::getVersionStamp()</c>
    /// (<c>cppcache/src/MapEntry.hpp:59</c>, pure virtual,
    /// <c>VersionStamp&amp;</c> by-reference for in-place mutation).
    /// Phase 2+ concurrency-checks feature 落地後,
    /// <see cref="VersionStamp"/> 才會帶 fields + <c>ProcessVersionTag</c>
    /// / <c>SetVersions</c> 方法。
    /// </summary>
    public virtual VersionStamp VersionStamp => throw new NotImplementedException(
        "MapEntry.VersionStamp: pending Phase 2+ concurrency-checks plumbing.");

    /// <summary>
    /// Per-entry LRU bookkeeping (LRU-list node + overflow persistence
    /// handle). Mirrors cppcache <c>MapEntry::getLRUProperties</c>
    /// (<c>cppcache/src/MapEntry.hpp:57</c>, pure virtual) — non-LRU entries
    /// throw (like <see cref="VersionStamp"/> for non-versioned); an
    /// LRU-variant entry overrides to return its
    /// <see cref="LRUEntryProperties"/>. No LRU-variant entry exists yet, so
    /// the base always throws; reached only on the overflow path (Phase 4).
    /// </summary>
    public virtual LRUEntryProperties LRUProperties => throw new NotImplementedException(
        "MapEntry.LRUProperties: non-LRU entry has none; pending Phase 2+ LRU-variant entry.");

    /// <summary>
    /// This entry's key (set once at construction, never reassigned).
    /// Mirrors cppcache <c>MapEntry::getKey</c> /
    /// <c>MapEntryImpl::getKeyI</c> (<c>cppcache/src/MapEntry.hpp:52</c>
    /// pure virtual / <c>cppcache/src/MapEntryImpl.hpp:56-58</c>) — the
    /// <c>shared_ptr&amp;</c> out-param becomes a return value, read-only
    /// (cppcache <c>m_key</c> is ctor-only, no setter). Concrete storage on
    /// <see cref="MapEntryImpl"/>; base throws like the sibling
    /// <see cref="VersionStamp"/> / <see cref="LRUProperties"/> NIEs.
    /// </summary>
    public virtual object Key => throw new NotImplementedException(
        "MapEntry.Key: pending Phase 2+ entry-key storage on MapEntryImpl.");

    /// <summary>
    /// Current entry value (<see langword="null"/> for
    /// tombstone / deleted-but-not-collected entries). Mirrors cppcache
    /// <c>MapEntry::getValue</c> / <c>setValue</c>
    /// (<c>cppcache/src/MapEntry.hpp:53-54</c>, pure virtual) — the two
    /// methods collapse to a single C# property; out-param
    /// (<c>shared_ptr&amp;</c>) → return value, setter handles the
    /// inbound shape. Concrete storage on <see cref="MapEntryImpl"/>.
    /// </summary>
    public abstract object? Value { get; set; }
}
