using System.Collections.Concurrent;
using System.Diagnostics;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal class ConcurrentEntriesMap(IServiceProvider serviceProvider, EntryFactory factory,
        bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    : EntriesMap
{

    static readonly ObjectFactory<ConcurrentEntriesMap> _objectFactory
    = ActivatorUtilities.CreateFactory<ConcurrentEntriesMap>(
        [typeof(EntryFactory), typeof(bool), typeof(LocalRegion), typeof(int)]);
    private readonly ConcurrentDictionary<object, MapEntry> _map = new();
    private int _size;

    public override Task ClearAsync(CancellationToken ct = default)
    {
        _map.Clear();
        Interlocked.Exchange(ref _size, 0);
        return Task.CompletedTask;
    }
    public override bool ContainsKey(object key)
        => _map.TryGetValue(key, out var entry) && !CacheableToken.IsTombstone(entry.Value);

    public static EntriesMap? Create(IServiceProvider serviceProvider, object factory,
        bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
        => _objectFactory(serviceProvider, [factory, concurrencyChecksEnabled, region, concurrency]);

    public override (MapEntry? Entry, object? Value) GetEntry(object key)
    {
        if (!_map.TryGetValue(key, out var entry))
        {
            return (null, null);
        }

        var value = entry.Value;
        if (value is null || CacheableToken.IsDestroyed(value) || CacheableToken.IsTombstone(value))
        {
            return (null, null);
        }
        return (entry, value);
    }

    public override Task<(MapEntry Entry, object? OldValue, bool IsUpdate)> PutAsync(object key, object newValue,
        int updateCount, int destroyTracker, VersionTag? versionTag, DataInput? delta = null,
        CancellationToken ct = default)
    {
        var isUpdate = false;
        MapEntry entry;
        object? oldValue = null;
        if (!_map.TryGetValue(key, out var existing))
        {
            if (delta is not null)
            {
                throw new GfErrTypeException(GfErrType.InvalidDelta);
            }

            var fresh = factory.NewEntry(key, newValue, updateCount, destroyTracker, versionTag);
            _map[key] = fresh;
            entry = fresh;
        }
        else
        {
            var meOldValue = existing.Value; 
            if (region.Attributes.ConcurrencyChecksEnabled)
            {
                if (versionTag is not null && existing is IVersionStamp stamp)
                {
                    stamp.Stamp.ProcessVersionTag(region, key, versionTag, deltaCheck: delta is not null);
                    stamp.Stamp.SetVersions(versionTag);
                }
            }

          
            if (CacheableToken.IsTombstone(meOldValue))
            {
                region._tombstoneList?.Erase(key);
          
                _map.TryRemove(key, out var tombstoned);
                var fresh = factory.NewEntry(key, newValue, updateCount, destroyTracker,
                    versionTag, carriedStamp: (tombstoned as IVersionStamp)?.Stamp);
                _map[key] = fresh;
                entry = fresh;                                          // ← writeback gap
                oldValue = null;
                isUpdate = false;
            }
            else
            {
                if (updateCount >= 0 || region.Attributes.ConcurrencyChecksEnabled)
                {
                    PutForTrackedEntry(existing, key, newValue, updateCount, delta);
                }
                else if (delta is not null)
                {
                    var existingValue = existing.Value;
                    if (existingValue is null
                        || CacheableToken.IsDestroyed(existingValue)
                        || CacheableToken.IsInvalid(existingValue)
                        || CacheableToken.IsTombstone(existingValue))
                    {
                        (region.Pool as ThinClientPoolDM)?.UpdateNotificationStats(false, TimeSpan.Zero);
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }

                   
                    if (CacheableToken.IsOverflowed(existingValue))
                    {
                        // TODO Phase 4 overflow: 同 PutForTrackedEntry 內的 dead branch —
                        //   要 await GetFromDiskAsync,連帶 caller chain 要 cascade async。
                        //   IsOverflowed token 在 overflow 寫入路徑接通前不會出現。
                        throw new NotImplementedException(
                            "ConcurrentEntriesMap.PutAsync delta path: overflow read-back pending IPersistenceManager (needs async cascade).");
                    }

                    // 既有 value 沒 implement IDelta → 沒法套
                    if (existingValue is not IDelta valueWithDelta)
                    {
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }

                    // cppcache try { fromDelta } catch (InvalidDeltaException) →
                    //   GF_INVALID_DELTA pipeline 訊號。使用者 IDelta impl 自行
                    //   throw InvalidDeltaException;我們在此邊界轉成 GfErrType。
                    try
                    {
                        if (region.Attributes.CloningEnabled)
                        {
                            // clone path:不動原物,套 delta 到 clone 後寫回
                            var tempVal = (IDelta)valueWithDelta.Clone();
                            // TODO: cppcache 包了 clock::now() 前後計時,成功後
                            //   m_poolDM->updateNotificationStats(true, elapsed)。
                            //   timing + 成功側 call 都還沒翻。
                            tempVal.FromDelta(delta);
                            existing.Value = tempVal;
                        }
                        else
                        {
                            // in-place path:直接 mutate 原物
                            // TODO: 同上 — cppcache 計時 + updateNotificationStats(true, elapsed)
                            //   還沒翻。
                            valueWithDelta.FromDelta(delta);
                            existing.Value = valueWithDelta;
                        }
                    }
                    catch (InvalidDeltaException)
                    {
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }
                }
                else
                {
                    existing.Value = newValue;
                }
                // SetVersions 已在上面 m_concurrencyChecksEnabled 區透過 stamp
                //   (existing.VersionStamp 的活引用) mutate 完成 — cppcache 因為
                //   VersionStamp 是 value-copy,需要再寫回一次;C# 是 reference
                //   type,省掉第二次 setVersions。
                //
                // Writeback 對兩條 branch 都一致:PutForTrackedEntry
                //   返回成功時也是這套 entry / oldValue / isUpdate (跟 inline
                //   path 同款),所以擺在 if/else if/else 外。
                entry = existing;
                oldValue = meOldValue;
                isUpdate = meOldValue is not null;
            }
        }

        // cppcache ConcurrentEntriesMap::put L116-118 — fresh insert / tombstone
        //   resurrection 才 ++m_size;純更新不算。
        if (!isUpdate)
        {
            Interlocked.Increment(ref _size);
        }
        return Task.FromResult((entry, oldValue, isUpdate));
    }
    public override Task<(MapEntry? Entry, object? OldValue)> RemoveAsync(object key, int updateCount,
        VersionTag? versionTag, bool afterRemote, CancellationToken ct = default)
    {
        // cppcache ConcurrentEntriesMap::remove + MapSegment::remove 兩層攤平。
        // ConcurrentEntriesMap dispatches by key hash; MapSegment 做真實工作。
        // .NET ConcurrentDictionary 已內建分段鎖,兩層收成一層。
        // cppcache `if (isEntryFound) --m_size` 收掉 — _map.Count 自帶。

        // ── cppcache MapSegment::remove L312-321 — concurrency-checks branch ─
        if (region.Attributes.ConcurrencyChecksEnabled)
        {
            // helper 維持 sync(純記憶體),這層 Task-wrap 即可。
            return Task.FromResult(RemoveWhenConcurrencyEnabled(key, updateCount, versionTag, afterRemote));
        }

        // ── cppcache L323-337 — happy path (no concurrency-checks) ─────────
        // m_spinlock + m_map.find + m_map.erase 三步,ConcurrentDictionary.TryRemove 一次完成。
        if (!_map.TryRemove(key, out var entry))
        {
            // cppcache: didn't unbind, probably no entry...
            //   destroyTrackers > 0 → m_destroyedKeys[key] = destroyTrackers + 1
            //   destroy-tracker bookkeeping 還沒做,跳過。
            // cppcache returns GF_CACHE_ENTRY_NOT_FOUND; 對應 (null, null)。
            return Task.FromResult<(MapEntry?, object?)>((null, null));
        }

        // ── cppcache L339-342 — updateCount race detection ────────────────
        // if (updateCount >= 0 && updateCount != entry->getUpdateCount())
        //   return GF_CACHE_ENTRY_UPDATED;
        // MapEntry.UpdateCount accessor 還沒擺 + AddTrackerForEntry NIE,
        //   tracker 子系統真上線時補。

        // ── cppcache L344-349 — getValueI + tombstone normalize ───────────
        var oldValue = entry.Value;
        if (CacheableToken.IsTombstone(oldValue))
        {
            oldValue = null;
        }

        // cppcache ConcurrentEntriesMap::remove L142-148 — isEntryFound 為 true
        //   (找到 + 真有 live value) → --m_size。tombstone-as-old 的情況 cppcache
        //   會在 segment 層回 GF_CACHE_ENTRY_NOT_FOUND,不走 -- 分支;對應 C# 的
        //   `oldValue != null` 判斷。
        if (oldValue is not null)
        {
            Interlocked.Decrement(ref _size);
        }

        // cppcache: `if (oldValue) me = entryImpl;` — entry 只在 value 非 null 時帶出。
        return Task.FromResult<(MapEntry?, object?)>((oldValue is null ? null : entry, oldValue));
    }

    public override Task<(MapEntry? Entry, object? OldValue)> InvalidateAsync(
        object key,
        VersionTag? versionTag = null,
        CancellationToken ct = default)
    {
        if (!_map.TryGetValue(key, out var existing))
        {
            // cppcache MapSegment::invalidate else-branch: key absent. Under
            //   concurrency-checks seed an INVALID token (so a later versioned
            //   update has a stamp to compare), count it, then report not-found.
            if (region.Attributes.ConcurrencyChecksEnabled)
            {
                _map[key] = factory.NewEntry(key, CacheableToken.Invalid,
                    updateCount: -1, destroyTracker: -1, versionTag);
                Interlocked.Increment(ref _size);
            }
            throw new GfErrTypeException(GfErrType.CacheEntryNotFound);
        }

        if (region.Attributes.ConcurrencyChecksEnabled
            && versionTag is not null
            && existing is IVersionStamp stamp)
        {
            stamp.Stamp.ProcessVersionTag(region, key, versionTag, deltaCheck: false);
            stamp.Stamp.SetVersions(versionTag);
        }

        var oldValue = existing.Value;
        if (CacheableToken.IsTombstone(oldValue))
        {
            throw new GfErrTypeException(GfErrType.CacheEntryNotFound);
        }

        existing.Value = CacheableToken.Invalid;
        IncrementUpdateCount(key, existing);

        // cppcache: me carried out only when there was a live old value; _size
        //   unchanged on a hit (the key stays, just its value is invalidated).
        return Task.FromResult<(MapEntry?, object?)>((oldValue is null ? null : existing, oldValue));
    }

    public override void RemoveTrackerForEntry(object key)
        => throw new NotImplementedException();

    public override int Count => _size;

    private void PutForTrackedEntry(
        MapEntry existing,
        object key,
        object newValue,
        int updateCount,
        DataInput? delta,
        VersionStamp? versionStamp = null)
    {
        // cppcache MapSegment::putForTrackedEntry (MapSegment.cpp:601-690).
        // Three-way branch on (updateCount, concurrencyChecksEnabled):
        //   Path A: updateCount < 0 || concurrencyChecksEnabled — non-tracked
        //           put OR concurrency-checks active
        //   Path B: updateCount == entry.UpdateCount — tracker hit
        //   Path C: counter mismatch — race-loser

        if (updateCount < 0 || region.Attributes.ConcurrencyChecksEnabled)
        {
            // ── cppcache L609-614 — hoist pool DM resolve once ──────────
            //   auto* thinClientRegion = dynamic_cast<ThinClientRegion*>(m_region);
            //   ThinClientPoolDM* m_poolDM = nullptr;
            //   if (thinClientRegion) {
            //     m_poolDM = dynamic_cast<ThinClientPoolDM*>(thinClientRegion->getDistMgr());
            //   }
            var poolDM = region.Pool as ThinClientPoolDM;

            // ── cppcache L616-668 — delta path / L670 — plain setValueI ──
            // 同等 delta 邏輯目前也 inline 在 Put 的 else-if branch;
            // 之後 reconcile 統一。delta 分支自己 setValue,所以跟下方
            // plain setValue 是互斥(cppcache `if (delta != nullptr) { ... } else { setValueI(newValue) }`)。
            if (delta is not null)
            {
                var oldValue = existing.Value;

                // 沒可套 delta 的值(空 / destroyed / invalid / tombstone)→
                //   退回 full object 抓回(caller 接 InvalidDelta fallback)。
                if (oldValue is null
                    || CacheableToken.IsDestroyed(oldValue)
                    || CacheableToken.IsInvalid(oldValue)
                    || CacheableToken.IsTombstone(oldValue))
                {
                    poolDM?.UpdateNotificationStats(false, TimeSpan.Zero);
                    throw new GfErrTypeException(GfErrType.InvalidDelta);
                }

                // overflow 到磁碟 → 回讀,撈不回視為 InvalidDelta。
                if (CacheableToken.IsOverflowed(oldValue))
                {
                    // TODO Phase 4 overflow: GetFromDiskAsync 已是 async,
                    //   這條 branch 進來需要 await,連帶 PutForTrackedEntry
                    //   要 cascade 成 async。本 branch 在 overflow 寫入路徑
                    //   接通前是 dead(IsOverflowed token 不會出現),先 NIE
                    //   標記,IPersistenceManager 落地時一起 async 化。
                    throw new NotImplementedException(
                        "ConcurrentEntriesMap.PutForTrackedEntry: overflow read-back pending IPersistenceManager (needs async cascade through PutForTrackedEntry → PutAsync).");
                }

                // 既有 value 沒 implement IDelta → 沒法套。
                if (oldValue is not IDelta valueWithDelta)
                {
                    throw new GfErrTypeException(GfErrType.InvalidDelta);
                }

                // cppcache try { fromDelta } catch (InvalidDeltaException) →
                //   GF_INVALID_DELTA pipeline 訊號。
                try
                {
                    if (region.Attributes.CloningEnabled)
                    {
                        // clone path:不動原物,套 delta 到 clone 後寫回。
                        var tempVal = (IDelta)valueWithDelta.Clone();
                        // cppcache: auto currTimeBefore = clock::now();
                        //   fromDelta(); updateNotificationStats(true, clock::now()-currTimeBefore);
                        var start = Stopwatch.GetTimestamp();
                        tempVal.FromDelta(delta);
                        poolDM?.UpdateNotificationStats(true, Stopwatch.GetElapsedTime(start));
                        existing.Value = tempVal;
                    }
                    else
                    {
                        // in-place path:直接 mutate 原物。
                        var start = Stopwatch.GetTimestamp();
                        valueWithDelta.FromDelta(delta);
                        poolDM?.UpdateNotificationStats(true, Stopwatch.GetElapsedTime(start));
                        existing.Value = valueWithDelta;
                    }
                }
                catch (InvalidDeltaException)
                {
                    throw new GfErrTypeException(GfErrType.InvalidDelta);
                }
            }
            else
            {
                // cppcache L670 — entryImpl->setValueI(newValue)
                existing.Value = newValue;
            }

            // ── cppcache L672-676 — concurrency-checks 後置 ──────────────
            // tombstone-list erase + setVersions 寫回。預設 caller 透過
            // existing.VersionStamp(reference type)mutate 完成;若 caller
            // 走 cppcache value-copy 模式傳獨立 stamp 進來,額外寫回一次。
            if (region.Attributes.ConcurrencyChecksEnabled)
            {
                region._tombstoneList?.Erase(key, cancelTask: true);
                // cppcache L675 — entryImpl->getVersionStamp().setVersions(versionStamp);
                if (versionStamp is not null && existing is IVersionStamp stamp)
                {
                    stamp.Stamp.SetVersions(versionStamp);
                }
            }

            // ── cppcache L677 — (void)incrementUpdateCount(key, entry) ──
            IncrementUpdateCount(key, existing);
            return;
        }

        // ── cppcache Paths B & C — tracker counter compare ──────────────
        // cppcache 兩條都 call removeTrackerForEntry(key, entry, entryImpl);
        // C# 收成單一 RemoveTrackerForEntry(key) 簽章。
        if (updateCount == existing.UpdateCount)
        {
            // Path B (cppcache L679-683):counter 相符,正常寫入後 drop tracker。
            existing.Value = newValue;
            RemoveTrackerForEntry(key);
            return;
        }

        // Path C (cppcache L684-689):entry 在 tracking 期間被改過,
        //   放棄寫入,不動 oldValue / MapEntry,只 drop tracker,
        //   並以 CacheEntryUpdated 訊號往上 — UpdateNoThrowAsync catch
        //   switch 接這個訊號。
        RemoveTrackerForEntry(key);
        throw new GfErrTypeException(GfErrType.CacheEntryUpdated);
    }

    private void IncrementUpdateCount(object key, MapEntry entry)
    {
        if (region.Attributes.ConcurrencyChecksEnabled) return;
        entry.IncrementUpdateCount(entry);
    }

    /// <summary>
    /// Concurrency-checks branch. Mirrors cppcache
    /// <c>MapSegment::removeWhenConcurrencyEnabled</c>
    /// (<c>cppcache/src/MapSegment.cpp:240</c>) — version-tag 比對 +
    /// tombstone 寫入 + <c>TombstoneList</c> 紀錄。concurrency-checks
    /// 真上線時實作。
    /// </summary>
    private (MapEntry? Entry, object? OldValue) RemoveWhenConcurrencyEnabled(
        object key,
        int updateCount,
        VersionTag? versionTag,
        bool afterRemote)
    {
        // cppcache MapSegment::removeWhenConcurrencyEnabled (MapSegment.cpp:240-303).
        // m_spinlock 收掉(ConcurrentDictionary 自帶)。err-codes ride exceptions
        // (ProcessVersionTag throws GfErrTypeException for version conflicts).

        if (_map.TryGetValue(key, out var entry))
        {
            // cppcache: versionStamp = entry->getVersionStamp();
            //   stamp 是 reference type,後續 mutate 對 entry 立即可見;
            //   cppcache 因為是 value-copy 才需要最後再 setVersions 寫回。
            // versionTag == null = 本地發起的 remove(eviction / local-destroy):
            //   沒有 server 版本可比,跳過 conflict check。對齊舊
            //   Internal/ConcurrentEntriesMap:241 的 `if (versionTag is not null)`
            //   guard —— 搬進 Entry/ 時這個 guard 掉了,導致每次 eviction 的
            //   local-destroy(versionTag 永遠 null)都打到 ProcessVersionTag 的
            //   Phase-2 NIE,連帶 heap-LRU 背景驅逐被吞掉、entry-LRU / destroy 直接紅。
            if (entry is IVersionStamp stamp && versionTag is not null)
            {
                // cppcache: processVersionTag err → 早退;C# 拋 GfErrTypeException,
                //   UpdateNoThrowAsync catch switch 接 (CacheConcurrentModification
                //   / CacheEntryUpdated)。cppcache 用 entry->getImplPtr()->getKeyI(keyPtr)
                //   只是因為 shared_ptr 語意拿不到原 key;我們直接用 key 參數。
                stamp.Stamp.ProcessVersionTag(region, key, versionTag, deltaCheck: false);
                stamp.Stamp.SetVersions(versionTag);
            }

            // cppcache: entryImpl->getValueI(oldValue); if (oldValue) me = entryImpl;
            //   entry 只在 value 非 null 時帶出 (見最後 return)。
            var oldValue = entry.Value;

            // cppcache: 以 tombstone 蓋過原 entry(走 putForTrackedEntry 是為了
            //   走 tracker / version-check 路徑),成功才把 entry 登錄到 tombstone
            //   list 等 GC。失敗 (race / version conflict) 透過 GfErrTypeException
            //   往上拋,跳過 TombstoneList.Add。
            PutForTrackedEntry(entry, key, CacheableToken.Tombstone, updateCount, delta: null);
            region._tombstoneList?.Add(entry); 

            if (CacheableToken.IsTombstone(oldValue))
            {
                // cppcache: 原本就已經是 tombstone,沒實際拿掉東西。
                //   afterRemote 容忍(server 已成功,本地只是同步)→ GF_NOERR;
                //   否則 GF_CACHE_ENTRY_NOT_FOUND。兩條路在 C# 都收成 (null, null)
                //   —— DestroyActions.LocalUpdateAsync 用 oldValue==null 判定不需要
                //   cleanup,等同 cppcache localUpdate 跳過 success-path 的效果。
                //   m_size 不動(tombstone 本來就沒算進 live count)。
                return (null, null);
            }

            // cppcache ConcurrentEntriesMap::remove L142-148 — live entry 被 tombstone
            //   覆蓋掉,m_size --(雖然 _map 物理上還在,logical entry 數少一個)。
            Interlocked.Decrement(ref _size);
            return (oldValue is null ? null : entry, oldValue);
        }

        // ── cppcache: entry not found ──────────────────────────────────────
        if (versionTag is not null)
        {
            // cppcache: putNoEntry(key, tombstone, mapEntry, -1, 0, versionTag);
            //   m_tombstoneList->add(mapEntry).
            //   為這個 key 種一個 tombstone,讓未來任何 update 都有 stamp 可比。
            //   -1 / 0 是 cppcache helper 對 tracker / destroyTracker 的固定填值。
            var mapEntry = factory.NewEntry(key, CacheableToken.Tombstone,
                updateCount: -1, destroyTracker: 0, versionTag);
            _map[key] = mapEntry;
            region._tombstoneList?.Add(mapEntry); 
        }

        // cppcache: afterRemote ? GF_NOERR : GF_CACHE_ENTRY_NOT_FOUND。
        //   兩條都收成 (null, null) — caller (DestroyActions) 現行走法
        //   不會區分這兩種 err code,success-path stats 照常打。之後真的
        //   接到 err pipeline 時,改成拋 sentinel 讓 LocalRegion::localUpdate
        //   的 early-return 對齊。
        return (null, null);
    }
}
