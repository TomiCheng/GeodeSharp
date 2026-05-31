using System.Threading;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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


    private readonly ILogger<LRUEntriesMap> _logger;

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
    private readonly EvictionController? _evictionController;

    /// <summary>cppcache <c>m_currentMapSize</c>: running heap-size total (heap LRU). 0 until heap LRU lands.</summary>
    private long _currentMapSize;

    /// <summary>cppcache <c>m_name</c>: region name (logging / eviction-controller registration).</summary>
    private string _name;

    /// <summary>
    /// Region back-ref(C# 加的)— cppcache 不需要,因為 EC 存 name 後透過
    /// <c>cache_-&gt;getRegion(name)</c> 回查;我們 EC 直接存 <see cref="RegionInternal"/>
    /// ref,所以 LRUEntriesMap 在 Register / DisposeAsync 兩端都要能拿到 region 本身。
    /// 同步存 <see cref="_name"/> 純為了 logging 對齊 cppcache。
    /// </summary>
    private readonly LocalRegion _region;

    /// <summary>cppcache <c>m_validEntries</c>: non-token entry count.</summary>
    private int _validEntries;

    /// <summary>cppcache <c>m_heapLRUEnabled</c>.</summary>
    private bool _heapLruEnabled;

    public LRUEntriesMap(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LRUAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
        : base(serviceProvider, factory, concurrencyChecksEnabled, region, concurrency)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<LRUEntriesMap>>();
        _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
        _limit = lruLimit;
        _region = region;
        _name = region.FullPath;
        _heapLruEnabled = heapLRUEnabled;
        _action = new Lazy<LRUAction>(() => LRUAction.NewLRUAction(serviceProvider, lruEvictionAction, region, this));
        if (_heapLruEnabled)
        {
            // cppcache LRUEntriesMap ctor (LRUEntriesMap.cpp:80-88) — gate
            // 是 `cImpl->getEvictionController() != nullptr`,在我們這邊
            // 等於 `_heapLruEnabled`(EntriesMapFactory.cs:62-64 已把
            // `prop.HeapLRULimitEnabled` 攤平成這個 flag)。
            _evictionController = serviceProvider.GetRequiredService<EvictionController>();
            _evictionController.RegisterRegion(_region);
            _logger.LogInformation(
                "Heap LRU eviction controller registered region {RegionName}",
                _name);
        }

    }

    public override async Task<(MapEntry Entry, object? OldValue, bool IsUpdate)> PutAsync(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null,
        CancellationToken ct = default)
    {
        MapEntry entry;
        object? oldValue;
        bool isUpdate;
        {
            (entry, oldValue, isUpdate) = await base.PutAsync(key, newValue, updateCount, destroyTracker, versionTag, delta, ct).ConfigureAwait(false);

            bool isOldValueToken = CacheableToken.IsToken(oldValue);
            if (CacheableToken.IsOverflowed(oldValue))
            {
                // cppcache reads the overflowed value back inline:
                //   oldValue = m_pmPtr->read(key, persistenceInfo);
                //   if (oldValue != nullptr) m_pmPtr->destroy(key, persistenceInfo);
                // IPersistenceManager is now async (ReadAsync / DestroyAsync),
                // but this Put override is synchronous (mirrors cppcache's sync
                // ConcurrentEntriesMap::put). We will NOT sync-over-async here
                // (banned by the async-first rule; risks threadpool deadlock) —
                // the whole overflow read-back / eviction path must itself become
                // async in Phase 4. Deferred with the rest of the overflow
                // accounting (cf. ValidEntriesSize() NIE below). Unreachable in
                // Phase 1.x: no overflow-to-disk write path exists yet, so
                // oldValue is never an overflow token.
                throw new NotImplementedException(
                    "LRUEntriesMap.Put: Phase 4 overflow read-back pending an async " +
                    "eviction path (IPersistenceManager.ReadAsync/DestroyAsync cannot " +
                    "be awaited from the synchronous Put override).");
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
    /// <inheritdoc />
    /// <remarks>
    /// Resets the heap-size accounting then delegates to the base map wipe.
    /// Mirrors cppcache <c>LRUEntriesMap::clear</c>
    /// (<c>cppcache/src/LRUEntriesMap.cpp:102-105</c>).
    /// </remarks>
    /// <summary>
    /// cppcache <c>LRUEntriesMap::close</c> (<c>LRUEntriesMap.cpp:94-100</c>):
    /// <code>
    /// void LRUEntriesMap::close() {
    ///   if (m_evictionControllerPtr != nullptr) {
    ///     m_evictionControllerPtr->incrementHeapSize(-m_currentMapSize);
    ///     m_evictionControllerPtr->unregisterRegion(m_name);
    ///   }
    ///   ConcurrentEntriesMap::close();
    /// }
    /// </code>
    /// Cache 拆解時由 <see cref="RegionInternal"/>.<c>DisposeAsync</c> 串下來,
    /// 把這個 region 從 cache-scoped <see cref="EvictionController"/> 名單退出。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        if (_evictionController is not null)
        {
            // cppcache LRUEntriesMap::close (LRUEntriesMap.cpp:94-100) — 先把
            // 本 region 累積過的 heap-size 從 EC 總和扣回,再 unregister。
            // `_currentMapSize` 在 UpdateMapSize 落地前永遠 0,扣 0 functional noop,
            // 但呼叫順序與 cppcache 一致,UpdateMapSize 接通時 zero-diff 立刻生效。
            _evictionController.IncrementHeapSize(-_currentMapSize);
            _evictionController.UnregisterRegion(_region);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        // cppcache LRUEntriesMap::clear (LRUEntriesMap.cpp:102-105):
        //   updateMapSize(-m_currentMapSize);
        //   ConcurrentEntriesMap::clear();
        //
        // cppcache's updateMapSize guards internally on m_evictionControllerPtr
        // (heap-LRU only); this port guards at the call site to match Put, so the
        // entry-count LRU path skips it (UpdateMapSize stays Phase-4 NIE because
        // EvictionController.IncrementHeapSize is NIE) and only base.ClearAsync() runs.
        if (_evictionController is not null)
        {
            UpdateMapSize(-_currentMapSize);
        }

        // Wipe the backing map + logical size (ConcurrentEntriesMap.ClearAsync).
        await base.ClearAsync(ct).ConfigureAwait(false);

        // NOTE: faithful to cppcache — clear() deliberately does NOT drain
        // lru_queue_ nor reset m_validEntries. The queue keeps stale MapEntry
        // handles (a later eviction pops them; the evict action no-ops on the
        // now-absent key — and eviction is itself NIE until Phase 2+), and
        // _validEntries is left as-is. Mirroring rather than "fixing" until
        // there's evidence the divergence matters (cppcache-scope-parity).
    }

    private void UpdateMapSize(long size)
    {
        // cppcache LRUEntriesMap::updateMapSize (LRUEntriesMap.cpp:476-483).
        // Caller(Put / Clear / DisposeAsync 的 heap-LRU 分支)都已經
        //   `if (_evictionController is not null)` 守過,直接 `!` 解 null。
        //   cppcache 自己的 TODO 也說「caller 已 null-check,內層判斷可移除」。
        // Interlocked.Add 對應 atomic increment — Put 跨 segment 可能並行,
        //   `_currentMapSize` 是 region 全域累計,要原子。cppcache 是 plain
        //   `m_currentMapSize += size`,在 segment 鎖外執行,接受 eventually-
        //   consistent;我們用 Interlocked 收緊。
        // 順序對齊 cppcache:先更 local,再 forward 給 EC。
        Interlocked.Add(ref _currentMapSize, size);
        _evictionController!.IncrementHeapSize(size);
    }
}
