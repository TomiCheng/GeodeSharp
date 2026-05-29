using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Builds <see cref="MapEntry"/> instances for an <see cref="EntriesMap"/>.
/// Base of the 4-way factory hierarchy (plain / Exp / LRU / LRU+Exp);
/// the base itself produces a plain entry. Mirrors cppcache
/// <c>EntryFactory</c> (<c>cppcache/src/MapEntryImpl.hpp:127</c>).
/// Skeleton only — <c>newMapEntry</c> body lands with the caching-enabled
/// phase (Phase 2+).
/// </summary>
internal class EntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
{
    IServiceProvider _ = serviceProvider;

    /// <summary>
    /// cppcache <c>m_concurrencyChecksEnabled</c>
    /// (<c>cppcache/src/MapEntryImpl.hpp:139</c>): whether per-entry
    /// version-tag tracking is enabled. Forwarded down to <c>newMapEntry</c>
    /// when subclass bodies land (Phase 2+).
    /// </summary>
    protected readonly bool ConcurrencyChecksEnabled = concurrencyChecksEnabled;

    static readonly ObjectFactory<EntryFactory> _objectFactory
    = ActivatorUtilities.CreateFactory<EntryFactory>([typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Pre-baked <see cref="ObjectFactory{T}"/> avoids
    /// per-call ctor resolution; same pattern as <see cref="LRUEntriesMap.Create"/>.
    /// </summary>
    /// <param name="nullconcurrencyChecksEnabled"></param>
    internal static EntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    {
        return _objectFactory(serviceProvider, [concurrencyChecksEnabled]);
    }
    /// <summary>
    /// Build a fresh <see cref="MapEntry"/> for <paramref name="key"/>.
    /// Mirrors cppcache <c>MapSegment::putNoEntry</c> ctor-time args
    /// (<c>cppcache/src/MapSegment.hpp:125-130</c>) folded into the
    /// factory: <paramref name="updateCount"/> + <paramref name="destroyTracker"/>
    /// seed concurrent-update bookkeeping; <paramref name="versionTag"/>
    /// seeds the new entry's stamp; <paramref name="carriedStamp"/> is
    /// non-<see langword="null"/> only on tombstone-resurrection — it
    /// carries the prior entry's version history into the replacement so
    /// distributed concurrency-checks stay coherent.
    /// </summary>
    public MapEntry NewEntry(object key, object newValue,
        int updateCount, int destroyTracker,
        VersionTag? versionTag, VersionStamp? carriedStamp = null)
    {
        // cppcache MapSegment::putNoEntry race-loser 早退 (僅在 !concurrencyChecks):
        //   updateCount >= 0 → caller (AddTrackerForEntry) 在 race-loser,GfErrType
        //     收成 throw GfErrTypeException(CacheEntryUpdated);UpdateNoThrowAsync
        //     catch switch 已在等。
        //   destroyTracker > 0 → 查 m_destroyedKeys、比較 update counter;destroy-
        //     tracker subsystem 整套 Phase 2+,先 NIE。
        if (!ConcurrencyChecksEnabled)
        {
            if (updateCount >= 0)
            {
                throw new GfErrTypeException(GfErrType.CacheEntryUpdated);
            }
            if (destroyTracker > 0)
            {
                throw new NotImplementedException(
                    "EntryFactory.NewEntry: destroy-tracker race detection pending Phase 2+ tracker subsystem.");
            }
        }

        // cppcache EntryFactory::newMapEntry — concurrency-checks 切兩種 entry 型別
        MapEntry entry = ConcurrencyChecksEnabled
            ? new VersionedMapEntryImpl()
            : new MapEntryImpl();
        // cppcache `m_entryFactory->newMapEntry(_, key, newEntry)` 之後 key 寫進
        //   m_key — C# 港 MapEntry 還沒 Key field,待 GetEntry 路徑要用時補。
        entry.Value = newValue;
        if (ConcurrencyChecksEnabled)
        {
            if (versionTag is not null)
            {
                entry.VersionStamp.SetVersions(versionTag);
            }
            else if (carriedStamp is not null)
            {
                entry.VersionStamp.SetVersions(carriedStamp);
            }
        }
        // cppcache `m_map.emplace(key, newEntry)` 留給 caller
        //   (ConcurrentEntriesMap.Put 已做 `_map[key] = fresh`)。
        return entry;
    }
}
