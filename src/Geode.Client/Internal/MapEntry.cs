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
/// land with the caching-enabled work.
/// </summary>
internal abstract class MapEntry
{
    /// <summary>
    /// Whether this entry already has an entry-expiry task scheduled.
    /// Mirrors cppcache
    /// <c>MapEntryImpl::getExpProperties().task_scheduled()</c>
    /// (<c>cppcache/src/MapEntry.hpp</c>, <c>ExpProperties</c> chain).
    /// Flat property here for NIE convenience; cppcache's
    /// two-step <c>ExpProperties</c> accessor will be reintroduced when
    /// the full expiry surface lands.
    /// </summary>
    public virtual bool IsExpiryTaskScheduled => throw new NotImplementedException(
        "MapEntry.IsExpiryTaskScheduled: pending expiry plumbing.");

    /// <summary>
    /// Expiration bookkeeping for this entry (last-accessed / last-modified
    /// timestamps + expiry-task handle). Mirrors cppcache
    /// <c>MapEntry::getExpProperties</c>
    /// (<c>cppcache/src/MapEntry.hpp:58</c>, pure virtual) — concrete storage
    /// on <see cref="MapEntryImpl"/> (every entry carries one, like cppcache's
    /// <c>m_expProp</c> member), so this is <see langword="abstract"/> rather
    /// than the base-throws pattern used by <see cref="LRUProperties"/>.
    /// </summary>
    public abstract ExpEntryProperties ExpProperties { get; }

    /// <summary>
    /// Per-entry concurrency-check version stamp. Mirrors cppcache
    /// <c>MapEntry::getVersionStamp</c>
    /// (<c>cppcache/src/MapEntry.hpp:59</c>, pure virtual,
    /// <c>VersionStamp&amp;</c> by-reference for in-place mutation).
    /// Non-versioned <see cref="MapEntryImpl"/> throws;
    /// <see cref="VersionedMapEntryImpl"/> returns its composed stamp.
    /// </summary>
    public abstract VersionStamp VersionStamp { get; }

    /// <summary>
    /// Per-entry LRU bookkeeping (LRU-list node + overflow persistence
    /// handle). Mirrors cppcache <c>MapEntry::getLRUProperties</c>
    /// (<c>cppcache/src/MapEntry.hpp:57</c>, pure virtual) — non-LRU entries
    /// throw (like <see cref="VersionStamp"/> for non-versioned); an
    /// LRU-variant entry overrides to return its
    /// <see cref="LRUEntryProperties"/>. No LRU-variant entry exists yet, so
    /// the base always throws; reached only on the overflow path.
    /// </summary>
    public virtual LRUEntryProperties LRUProperties => throw new NotImplementedException(
        "MapEntry.LRUProperties: non-LRU entry has none; pending LRU-variant entry.");

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
        "MapEntry.Key: pending entry-key storage on MapEntryImpl.");

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

    /// <summary>
    /// Per-entry tracker counter used by the put/remove pipeline to detect
    /// a race between a tracked op and an intervening update. Mirrors
    /// cppcache <c>MapEntry::getUpdateCount</c>
    /// (<c>cppcache/src/MapEntry.hpp:111</c>, pure virtual). Storage lands
    /// on <see cref="MapEntryImpl"/> when the tracker subsystem is wired.
    /// </summary>
    public abstract int UpdateCount { get; }

    /// <summary>
    /// Bumps this entry's tracker counter (<see cref="UpdateCount"/>) by one.
    /// Mirrors cppcache <c>MapEntry::incrementUpdateCount</c>
    /// (<c>cppcache/src/MapEntry.hpp:99</c>, pure virtual). cppcache 用 placement
    /// new 改 vptr 把 entry 「morph」 到 <c>MapEntryT&lt;..., UPDATE_COUNT+1&gt;</c>
    /// 新型別,並透過 <c>newEntry</c> out-param 在 MAX boundary 時回傳重新分配
    /// 的 <c>TrackedMapEntry</c>;C# 沒這把戲(也沒 boundary morph),純粹是
    /// 一個 <see cref="UpdateCount"/>++,所以 cppcache 的 <c>shared_ptr&amp;</c>
    /// out-param + <c>int</c> 回傳值在 C# 都收掉。
    /// </summary>
    public abstract void IncrementUpdateCount();

    /// <summary>
    /// Any cleanup required for this entry (e.g. removing from the LRU list).
    /// Mirrors cppcache <c>MapEntry::cleanup</c>
    /// (<c>cppcache/src/MapEntry.hpp:116</c>, pure virtual). No-op for
    /// non-LRU entries(<see cref="MapEntryImpl"/> override 空 body 對映
    /// cppcache <c>MapEntryImpl::cleanup() override {}</c>);LRU-variant
    /// entries override to unlink from the LRU queue.
    /// </summary>
    public abstract void Cleanup(CacheEventFlags eventFlags);
}
