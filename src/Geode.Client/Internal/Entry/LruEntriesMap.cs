using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal.Entry;

internal class LruEntriesMap :
    ConcurrentEntriesMap
{
    static readonly ObjectFactory<LruEntriesMap> _objectFactory
    = ActivatorUtilities.CreateFactory<LruEntriesMap>(
        [typeof(EntryFactory), typeof(LocalRegion), typeof(LruAction.Action), typeof(int),
                typeof(bool), typeof(int), typeof(bool)]);

    public static LruEntriesMap? Create(IServiceProvider serviceProvider, EntryFactory factory,
        LocalRegion region, LruAction.Action action, int lruLimit, bool concurrencyChecksEnabled,
        int concurrency, bool heapLRUEnabled)
        => _objectFactory(serviceProvider, [factory, region, action, lruLimit,
            concurrencyChecksEnabled, concurrency, heapLRUEnabled]);



    private readonly ILogger<LruEntriesMap> _logger;

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
    private readonly Lazy<LruAction> _action;

    /// <summary>cppcache <c>lru_queue_</c>: MRU-ordered entry queue (value member, always constructed).</summary>
    private readonly LruQueue _lruQueue = new();

    /// <summary>cppcache <c>m_limit</c>: entry-count (or heap) eviction cap.</summary>
    private int _limit;

    /// <summary>cppcache <c>m_pmPtr</c>: overflow-to-disk manager; null until <see cref="SetPersistenceManager"/>.</summary>
    private IPersistenceManager? _persistenceManager;

    /// <summary>
    /// Attach the overflow-to-disk manager. Mirrors cppcache
    /// <c>LRUEntriesMap::setPersistenceManager</c>
    /// (<c>cppcache/src/LRUEntriesMap.hpp:98-100</c>) — forwarded from
    /// <see cref="LocalRegion.InitializeAsync"/> when DiskPolicy == Overflows.
    /// </summary>
    internal void SetPersistenceManager(IPersistenceManager pmPtr) => _persistenceManager = pmPtr;

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

    public LruEntriesMap(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LruAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
        : base(serviceProvider, factory, concurrencyChecksEnabled, region, concurrency)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<LruEntriesMap>>();
        _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
        _limit = lruLimit;
        _region = region;
        _name = region.FullPath;
        _heapLruEnabled = heapLRUEnabled;
        _action = new Lazy<LruAction>(() => LruAction.NewLruAction(serviceProvider, lruEvictionAction, region, this));
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

        await ProcessLruAsync(ct).ConfigureAwait(false);
        return (entry, oldValue, isUpdate);
    }

    /// <summary>
    /// Remove override that keeps the heap-LRU bookkeeping in sync. Mirrors
    /// cppcache <c>LRUEntriesMap::remove</c>
    /// (<c>cppcache/src/LRUEntriesMap.cpp:432-474</c>) — base remove, then
    /// pull the entry out of the MRU queue, drop the valid-entry count, and
    /// decrement the running heap total by key + value size.
    /// </summary>
    /// <remarks>
    /// 整段 gate 在 <c>_evictionController is not null</c>(= heap-LRU 開)。
    /// entry-count LRU path(controller 為 null)維持 base 行為 — queue 留
    /// stale handle、不動 heap 帳(對齊現有 Clear() 的容忍註解)。
    /// <para>
    /// 關鍵:沒有這個 decrement,eviction destroy 走回本 method 卻不扣
    /// <c>_heapSize</c>,EvictionController 會永遠看到 over-limit、無限驅逐。
    /// cppcache 正是在這裡用 <c>updateMapSize(-sizeToRemove)</c> 讓控制迴圈收斂。
    /// </para>
    /// </remarks>
    public override async Task<(MapEntry? Entry, object? OldValue)> RemoveAsync(
        object key, int updateCount, VersionTag? versionTag, bool afterRemote,
        CancellationToken ct = default)
    {
        var (entry, oldValue) = await base
            .RemoveAsync(key, updateCount, versionTag, afterRemote, ct)
            .ConfigureAwait(false);

        if (_evictionController is not null && entry is not null && oldValue is not null)
        {
            // cppcache L444-448: 從 MRU 佇列拔掉 + 調整 valid-entry 計數。
            // Eviction 路徑下 entry 已被 EvictionHelperAsync Pop 過,這裡的
            // Remove 是無害 no-op;user 直接 destroy heap-LRU region 時才做真正清理。
            _lruQueue.Remove(entry);
            if (!CacheableToken.IsToken(oldValue))
            {
                --_validEntries;
            }

            // cppcache L465-468: 用 key + value 的 size 把 heap 總和扣回去。
            var sizeToRemove = _serializationRegistry.CheckAndGetObjectSize(key)
                             + _serializationRegistry.CheckAndGetObjectSize(oldValue);
            UpdateMapSize(-sizeToRemove);
        }

        return (entry, oldValue);
    }

    /// <summary>
    /// Count of non-token (valid) entries. Mirrors cppcache
    /// <c>LRUEntriesMap::validEntriesSize</c>
    /// (<c>cppcache/src/LRUEntriesMap.hpp:124</c>,
    /// <c>return m_validEntries;</c>). Used by <see cref="MustEvict"/>'s
    /// overflow branch.
    /// </summary>
    /// <remarks>
    /// <c>_validEntries</c> is maintained by <see cref="PutAsync"/> (++ on a
    /// fresh non-token insert) and <see cref="EvictionHelperAsync"/> (-- when an
    /// overflow action spills an entry to disk). The overflow branch of
    /// <see cref="MustEvict"/> needs it because overflowed entries stay in the
    /// backing map as tokens — <see cref="EntriesMap.Count"/> never drops, so
    /// only the valid-entry count converges the eviction loop.
    /// </remarks>
    private int ValidEntriesSize() => _validEntries;

    private bool MustEvict()
    {
        // cppcache LRUEntriesMap::mustEvict (LRUEntriesMap.hpp:110-123).
        // Overflow path MUST compare validEntriesSize(), NOT size(): an
        // overflowed entry stays in the backing map as a token (size()
        // unchanged), so a size()-based loop never drops below the limit and
        // ProcessLruAsync spins forever (queue drains, EvictionHelperAsync
        // returns false, but the while-condition stays true). The valid-entry
        // count drops one per overflow → the loop converges.
        if (_action.Value.Overflows)
        {
            return ValidEntriesSize() > _limit;
        }
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
    private async Task<bool> EvictionHelperAsync(CancellationToken ct = default)
    {
        // cppcache LRUEntriesMap::evictionHelper (LRUEntriesMap.cpp:169-185).
        // GfErrType loop-control collapses to bool: true = evicted (keep going),
        // false = stop (GF_ENOENT empty queue / GF_DISKFULL overflow failed).
        var entry = _lruQueue.Pop();
        if (entry is null)
        {
            return false;   // GF_ENOENT: nothing to evict.
        }

        // m_action->evict(entry): LOCAL_DESTROY → region.DestroyNoThrowAsync
        //   (still NIE on LRULocalDestroyAction.EvictAsync, pending
        //   DestroyNoThrowAsync + MapEntry.Key).
        var evictDone = await _action.Value.EvictAsync(entry, ct).ConfigureAwait(false);

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
    private async Task ProcessLruAsync(CancellationToken ct = default)
    {
        while (MustEvict())
        {
            await EvictionHelperAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evict up to <paramref name="numEntriesToEvict"/> entries from the LRU
    /// head. Mirrors cppcache <c>LRUEntriesMap::processLRU(int32_t)</c>
    /// overload (<c>cppcache/src/LRUEntriesMap.cpp:187-198</c>) — called from
    /// <see cref="LocalRegion.EvictAsync"/> with the controller-computed count.
    /// </summary>
    /// <remarks>
    /// 跟 zero-arg overload 區別:這條走「指定數量」(EvictionController 算出
    /// 的 entriesToEvict),zero-arg 是「踢到不超過 limit 為止」。
    /// cppcache 的 <c>int32_t evicted = 0; ...; evicted++</c> 是 dead local
    /// (沒 log 讀它,純為 future LOGFINE 預留),C# 鏡像時 drop。
    /// </remarks>
    internal async Task ProcessLruAsync(int numEntriesToEvict, CancellationToken ct = default)
    {
        for (var i = 0; i < numEntriesToEvict; i++)
        {
            // cppcache `m_validEntries > 0 && size() > 0` guard —
            //   tombstones-only 殘留時 _validEntries 是 0(tombstone 不算 valid),
            //   保護無限 loop。EvictionHelperAsync 的回傳 false(queue 空)
            //   cppcache 並未 break,我們也照鏡像不 break。
            if (_validEntries <= 0 || Count <= 0)
            {
                break;
            }
            await EvictionHelperAsync(ct).ConfigureAwait(false);
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

    internal void UpdateMapSize(long size)
    {
        // cppcache LRUEntriesMap::updateMapSize (LRUEntriesMap.cpp:476-483) —
        // 內層 guard `if (m_evictionControllerPtr != nullptr)` 照搬。多數 caller
        // (Put / Clear / DisposeAsync)已在 call site 守過(冗餘但無害);但
        // LRUOverFlowToDiskAction.EvictAsync 沒守,而 overflow region 的
        // _evictionController 是 null(EntriesMapFactory OVERFLOWS 分支不設
        // heapLRUEnabled)→ 沒這個 guard 會 NRE。overflow 不參與 heap 帳,no-op 正確。
        if (_evictionController is null)
        {
            return;
        }

        // Interlocked.Add 對應 atomic increment — Put 跨 segment 可能並行,
        //   `_currentMapSize` 是 region 全域累計,要原子。cppcache 是 plain
        //   `m_currentMapSize += size`,在 segment 鎖外執行,接受 eventually-
        //   consistent;我們用 Interlocked 收緊。
        // 順序對齊 cppcache:先更 local,再 forward 給 EC。
        Interlocked.Add(ref _currentMapSize, size);
        _evictionController.IncrementHeapSize(size);
    }
}
