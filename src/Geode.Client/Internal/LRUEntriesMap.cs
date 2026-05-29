using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// LRU-bounded <see cref="ConcurrentEntriesMap"/>: tracks per-entry LRU
/// order and evicts (or overflows to disk) once <c>lruLimit</c> is hit.
/// Mirrors cppcache <c>LRUEntriesMap</c>
/// (<c>cppcache/src/LRUEntriesMap.hpp:45</c>). Skeleton only — members
/// land with the LRU subsystem (Phase 2+: <c>LRUQueue</c>, <c>LRUAction</c>
/// strategy, eviction triggers).
/// </summary>
internal sealed class LRUEntriesMap : ConcurrentEntriesMap
{
    static readonly ObjectFactory<LRUEntriesMap> _objectFactory
        = ActivatorUtilities.CreateFactory<LRUEntriesMap>(
            [typeof(EntryFactory), typeof(LocalRegion), typeof(LRUAction.Action), typeof(int),
                typeof(bool), typeof(int), typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Same pattern as <see cref="LocalRegion.Create"/> —
    /// pre-baked <see cref="ObjectFactory{T}"/> avoids per-call ctor
    /// resolution. <c>new</c> hides the inherited
    /// <see cref="ConcurrentEntriesMap.Create"/>: the bases are independent
    /// static factories (no virtual dispatch on static methods), each builds
    /// its own concrete type.
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="lruEvictionAction"></param>
    /// <param name="lruLimit"></param>
    /// <param name="concurrencyChecksEnabled"></param>
    /// <param name="concurrency"></param>
    /// <param name="heapLRUEnabled"></param>
    internal static LRUEntriesMap Create(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LRUAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
    {
        return _objectFactory(serviceProvider, [factory, region, lruEvictionAction, lruLimit,
            concurrencyChecksEnabled, concurrency, heapLRUEnabled]);
    }

    readonly SerializationRegistry _serializationRegistry;

    // ── cppcache LRUEntriesMap members (LRUEntriesMap.hpp:50-58) ──
    // Mirror 1:1. Ported types use the real type (LRUAction); the rest
    // (LRUQueue / PersistenceManager / EvictionController) park as object?
    // until ported. sealed class → private (protected would be CS0628);
    // CS0169 suppressed because these are intentional mirror placeholders
    // read only when LRU eviction / heap LRU land — same role as
    // LocalRegion's protected mirror fields, which dodge the warning by
    // being protected on a non-sealed class.
    /// <summary>cppcache <c>m_action</c>: eviction strategy, built from <c>lruEvictionAction</c> via <c>newLRUAction</c> (not wired → null until Phase 2+).</summary>
    private readonly Lazy<LRUAction> _action;

    /// <summary>cppcache <c>lru_queue_</c>: MRU-ordered entry queue (value member, always constructed).</summary>
    private readonly LRUQueue _lruQueue = new();

    /// <summary>cppcache <c>m_limit</c>: entry-count (or heap) eviction cap.</summary>
    private int _limit;

    /// <summary>cppcache <c>m_pmPtr</c>: overflow-to-disk manager; null until set (Phase 4).</summary>
    private IPersistenceManager? _persistenceManager;

    /// <summary>cppcache <c>m_evictionControllerPtr</c>: heap-LRU global controller; null until heap LRU registers.</summary>
    private EvictionController? _evictionController;

    /// <summary>cppcache <c>m_currentMapSize</c>: running heap-size total (heap LRU). 0 until heap LRU lands.</summary>
    private long _currentMapSize;

    /// <summary>cppcache <c>m_name</c>: region name (logging / eviction-controller registration).</summary>
    private string _name;

    /// <summary>cppcache <c>m_validEntries</c>: non-token entry count.</summary>
    private int _validEntries;

    /// <summary>cppcache <c>m_heapLRUEnabled</c>.</summary>
    private bool _heapLruEnabled;

    public LRUEntriesMap(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LRUAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
        : base(serviceProvider, factory, concurrencyChecksEnabled, region, concurrency)
    {
        _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
        _limit = lruLimit;
        _name = region.FullPath;
        _heapLruEnabled = heapLRUEnabled;
        _action = new Lazy<LRUAction>(() => LRUAction.NewLRUAction(serviceProvider, lruEvictionAction, region, this));


    }

    public override (MapEntry Entry, object? OldValue, bool IsUpdate) Put(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null)
    {
        MapEntry entry;
        object? oldValue;
        bool isUpdate;
        {
            (entry, oldValue, isUpdate) = base.Put(key, newValue, updateCount, destroyTracker, versionTag, delta);

            bool isOldValueToken = CacheableToken.IsToken(oldValue);
            if (CacheableToken.IsOverflowed(oldValue))
            {
                var persistenceInfo = entry.LRUProperties.PersistenceInfo;
                oldValue = _persistenceManager!.Read(key, persistenceInfo);
                if (oldValue is not null)
                {
                    _persistenceManager.Destroy(key, persistenceInfo);
                }
            }

            // TODO:  when can newValue be a token ??
            if (CacheableToken.IsToken(newValue) && !isOldValueToken)
            {
                --_validEntries;
            }
            if (!CacheableToken.IsToken(newValue) && isOldValueToken)
            {
                ++_validEntries;
            }


            // Add new entry to LRU list
            if (isUpdate == false)
            {
                ++_validEntries;
                var (mePtr, _) = GetEntry(key);
                _lruQueue.Push(mePtr!);
                entry = mePtr!;
            }
            else
            {
                if (!CacheableToken.IsToken(newValue) && isOldValueToken)
                {
                    var (mePtr, _) = GetEntry(key);
                    _lruQueue.Push(mePtr!);
                    entry = mePtr!;
                }
            }
        }
        if (_evictionController is not null)
        {
            var newSize = _serializationRegistry.CheckAndGetObjectSize(newValue);

            if (isUpdate == false)
            {
                newSize += _serializationRegistry.CheckAndGetObjectSize(key);
            }
            else
            {
                newSize -= _serializationRegistry.CheckAndGetObjectSize(oldValue);
              
            }
            UpdateMapSize(newSize);
        }

        ProcessLru();
        return (entry, oldValue, isUpdate);
    }

    /// <summary>
    /// Count of non-token (valid) entries. Mirrors cppcache
    /// <c>LRUEntriesMap::validEntriesSize</c>
    /// (<c>cppcache/src/LRUEntriesMap.hpp:124</c>,
    /// <c>return m_validEntries;</c>). Used by <see cref="MustEvict"/>'s
    /// overflow branch.
    /// </summary>
    /// <remarks>
    /// NIE for now: trivially <c>_validEntries</c>, but the token-aware
    /// <c>_validEntries</c> accounting only matters on the overflow path
    /// (Phase 4) and isn't exercised first-cut — flag rather than expose a
    /// not-yet-trustworthy count.
    /// </remarks>
    private int ValidEntriesSize() => throw new NotImplementedException(
        "LRUEntriesMap.ValidEntriesSize: pending Phase 4 overflow path (return _validEntries).");

    private bool MustEvict()
    {
        if (_heapLruEnabled && _limit == 0)
        {
            return false;
        }
        return Count > _limit;
    }

    /// <summary>
    /// Pop the LRU (head) entry and apply the eviction action to it; returns
    /// whether the caller should keep evicting. Mirrors cppcache
    /// <c>LRUEntriesMap::evictionHelper</c>
    /// (<c>cppcache/src/LRUEntriesMap.cpp:169-185</c>).
    /// </summary>
    /// <remarks>
    /// cppcache returns <c>GfErrType</c> as a loop-control signal
    /// (<c>GF_NOERR</c> keep going / <c>GF_ENOENT</c> queue empty /
    /// <c>GF_DISKFULL</c> overflow failed) — collapsed to a C# <c>bool</c>
    /// (<see langword="true"/> = evicted, continue). NIE until eviction
    /// lands: pop <see cref="_lruQueue"/>, null → stop; otherwise apply
    /// <see cref="_action"/> (<c>LOCAL_DESTROY</c> →
    /// <see cref="ConcurrentEntriesMap.TryEvictEntry"/>).
    /// </remarks>
    private bool EvictionHelper()
    {
        // cppcache LRUEntriesMap::evictionHelper (LRUEntriesMap.cpp:169-185).
        // GfErrType loop-control collapses to bool: true = evicted (keep going),
        // false = stop (GF_ENOENT empty queue / GF_DISKFULL overflow failed).
        var entry = _lruQueue.Pop();
        if (entry is null)
        {
            return false;   // GF_ENOENT: nothing to evict.
        }

        // m_action->evict(entry): LOCAL_DESTROY → region.DestroyNoThrow
        //   (still NIE on LRULocalDestroyAction.Evict, pending DestroyNoThrow
        //   + MapEntry.Key).
        var evictDone = _action.Value.Evict(entry);

        // overflow-only valid-count adjust (deferred path).
        if (_action.Value.Overflows && evictDone)
        {
            --_validEntries;
        }

        // GF_DISKFULL: overflow write failed → stop.
        return evictDone;
    }

    /// <summary>
    /// Evict from the LRU (head) end while the map is over its limit.
    /// Mirrors cppcache <c>LRUEntriesMap::processLRU</c>
    /// (<c>cppcache/src/LRUEntriesMap.cpp:161-167</c>):
    /// <code>while (mustEvict()) evictionHelper();</code>
    /// </summary>
    /// <remarks>
    /// First cut folds <see cref="MustEvict"/> + <see cref="EvictionHelper"/>
    /// into the entry-count + LOCAL_DESTROY loop; heap-LRU branches +
    /// non-destroy actions deferred.
    /// </remarks>
    private void ProcessLru()
    {
        while (MustEvict())
        {
            EvictionHelper();
        }
    }

    /// <summary>
    /// Add a heap-size delta to the running total and report it to the
    /// eviction controller. Mirrors cppcache
    /// <c>LRUEntriesMap::updateMapSize</c>
    /// (<c>cppcache/src/LRUEntriesMap.cpp:476-483</c>):
    /// <code>
    /// if (m_evictionControllerPtr != nullptr) {
    ///   m_currentMapSize += size;
    ///   m_evictionControllerPtr->incrementHeapSize(size);
    /// }
    /// </code>
    /// </summary>
    /// <remarks>
    /// Heap-LRU only — guarded by a non-null <see cref="_evictionController"/>,
    /// so the entry-count path never reaches it. NIE until heap LRU lands
    /// (needs <see cref="EvictionController.IncrementHeapSize"/>, also NIE).
    /// </remarks>
    private void UpdateMapSize(long size)
    {
        throw new NotImplementedException(
            "LRUEntriesMap.UpdateMapSize: pending Phase 4 heap-LRU (_currentMapSize += size; EvictionController.IncrementHeapSize).");
        // cppcache LRUEntriesMap::updateMapSize (LRUEntriesMap.cpp:476-483):
        //
        //   void LRUEntriesMap::updateMapSize(int64_t size) {
        //     // TODO: check and remove null check since this has already been done
        //     // by all the callers
        //     if (m_evictionControllerPtr != nullptr) {
        //       m_currentMapSize += size;
        //       m_evictionControllerPtr->incrementHeapSize(size);
        //     }
        //   }
        //
        // 翻譯備忘:
        // - `if (m_evictionControllerPtr != nullptr)` → if (_evictionController is not null)。
        //   cppcache 註解自己也說 caller 都查過了(Put 的 heap 區已包在
        //   `if (_evictionController is not null)` 內),這層 null check 其實冗餘,
        //   但留著對齊。
        // - m_currentMapSize += size → _currentMapSize += size。
        // - m_evictionControllerPtr->incrementHeapSize(size) → 跨型別 call,落在
        //   EvictionController.IncrementHeapSize(已 NIE,Phase 4)。這裡只 forward,
        //   別把它 body 貼進來。
    }
}
