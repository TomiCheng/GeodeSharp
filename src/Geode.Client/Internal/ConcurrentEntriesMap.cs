using System.Collections.Concurrent;
using System.Diagnostics;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Concurrent <see cref="EntriesMap"/> implementation. Mirrors cppcache
/// <c>ConcurrentEntriesMap</c>
/// (<c>cppcache/src/ConcurrentEntriesMap.hpp</c>).
/// </summary>
/// <remarks>
/// cppcache's <c>m_segments[]</c> + <c>MapSegment.m_map</c> + per-segment
/// spinlock / recursive_mutex / rehash machinery collapses onto a single
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> here — .NET's BCL
/// already does the striping (≈ <c>4 × Environment.ProcessorCount</c>
/// internal lock buckets) plus lock-free reads. The class itself stays
/// for the non-segment responsibilities (tombstone list, destroy tracker,
/// region back-ref, expiry manager) which arrive when caching-enabled lands.
/// </remarks>
internal class ConcurrentEntriesMap(IServiceProvider serviceProvider,
    EntryFactory factory, bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    : EntriesMap
{

    static readonly ObjectFactory<ConcurrentEntriesMap> _objectFactory
        = ActivatorUtilities.CreateFactory<ConcurrentEntriesMap>(
            [typeof(EntryFactory), typeof(bool), typeof(LocalRegion), typeof(int)]);
    IServiceProvider _ = serviceProvider;

    /// <summary>
    /// Backing store. Replaces cppcache <c>MapSegment[] m_segments</c>
    /// + per-segment <c>unordered_map</c> + manual lock striping.
    /// </summary>
    // TODO: ctor will take (concurrency, initialCapacity) from
    //   RegionAttributes once EntriesMapFactory's plain branch is wired.
    //   Default-construct for now so Count works against an empty map.
    private readonly ConcurrentDictionary<object, MapEntry> _map = new();

    /// <summary>
    /// Logical entry count, tracked separately from <see cref="_map"/>.Count
    /// so tombstone entries don't inflate it. Mirrors cppcache atomic
    /// <c>m_size</c> (<c>ConcurrentEntriesMap.hpp:46</c>) — Put / Create /
    /// Invalidate ++ on fresh insert, Remove -- on real entry removal.
    /// </summary>
    private int _size;

    /// <summary>
    /// Bumps the tracker counter on <paramref name="entry"/> (or rebinds it
    /// when destroy-tracking forces a fresh MapEntry under
    /// <paramref name="key"/>). Mirrors cppcache
    /// <c>MapSegment::incrementUpdateCount</c>
    /// (<c>cppcache/src/MapSegment.hpp:82-96</c>) — disabled when
    /// concurrency-checks are on (versioning takes over).
    /// </summary>
    private void IncrementUpdateCount(object key, MapEntry entry)
    {
        // cppcache MapSegment::incrementUpdateCount (MapSegment.hpp:82-96):
        //   "This function is disabled if concurrency checks are enabled.
        //    The versioning changes takes care of the version and no need
        //    for tracking the entry"
        if (region.Attributes.ConcurrencyChecksEnabled) return;

        // cppcache: entry->incrementUpdateCount(newEntry);
        //   if (newEntry != nullptr) { m_map.emplace(key, newEntry); entry = newEntry; }
        //   rebind path 在 C# 是 dead code(沒 placement-new boundary morph),
        //   回傳 bool「is rebound?」也跟著收掉,直接 void。
        entry.IncrementUpdateCount();
    }

    /// <summary>
    /// Tracked-entry update path. Mirrors cppcache
    /// <c>MapSegment::putForTrackedEntry</c>
    /// (<c>cppcache/src/MapSegment.cpp:601-690</c>) — MapSegment collapse
    /// 後落腳在這。三條 path 都已翻譯(Path A delta / non-delta、Path B
    /// 計數對齊、Path C race-loser);深處仍仰賴 NIE
    /// (<c>MapEntry.UpdateCount</c> getter、<see cref="RemoveTrackerForEntry"/>、
    /// <see cref="IncrementUpdateCount"/>、<see cref="VersionStamp.SetVersions(VersionStamp)"/>),
    /// 走進對應路徑時會炸。同等 delta 邏輯目前也 inline 在 <see cref="Put"/>
    /// 的 <c>else if (delta is not null)</c> 分支,之後 reconcile 收成單一處。
    /// </summary>
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
                    oldValue = GetFromDisk(key, existing);
                    if (oldValue is null)
                    {
                        poolDM?.UpdateNotificationStats(false, TimeSpan.Zero);
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }
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
                if (versionStamp is not null)
                {
                    existing.VersionStamp.SetVersions(versionStamp);
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
            var versionStamp = entry.VersionStamp;
            if (versionTag is not null)
            {
                // cppcache: processVersionTag err → 早退;C# 拋 GfErrTypeException,
                //   UpdateNoThrowAsync catch switch 接 (CacheConcurrentModification
                //   / CacheEntryUpdated)。cppcache 用 entry->getImplPtr()->getKeyI(keyPtr)
                //   只是因為 shared_ptr 語意拿不到原 key;我們直接用 key 參數。
                versionStamp.ProcessVersionTag(region, key, versionTag, deltaCheck: false);
                versionStamp.SetVersions(versionTag);
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

    /// <summary>
    /// DI-aware factory. Same pattern as <see cref="LocalRegion.Create"/> —
    /// pre-baked <see cref="ObjectFactory{T}"/> avoids per-call ctor
    /// resolution.
    /// </summary>
    /// <param name="factory"></param>
    /// <param name="concurrencyChecksEnabled"></param>
    /// <param name="concurrency"></param>
    internal static ConcurrentEntriesMap Create(IServiceProvider serviceProvider,
        EntryFactory factory, bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    {
        return _objectFactory(serviceProvider, [factory, concurrencyChecksEnabled, region, concurrency]);
    }

    /// <summary>
    /// Mirrors cppcache <c>ConcurrentEntriesMap::size()</c>
    /// (<c>ConcurrentEntriesMap.cpp:182</c>) — returns the tracked
    /// <see cref="_size"/> counter (NOT <see cref="_map"/>.Count, because
    /// tombstones live in the map but don't count as live entries).
    /// </summary>
    internal override int Count => _size;

    /// <inheritdoc />
    public override bool ContainsKey(object key) =>
        _map.TryGetValue(key, out var entry) && !CacheableToken.IsTombstone(entry.Value);

    /// <inheritdoc />
    public override (MapEntry? Entry, object? Value) GetEntry(object key)
    {
        // cppcache ConcurrentEntriesMap::getEntry → segmentFor → MapSegment::getEntry
        //   (MapSegment.cpp:379-402). m_spinlock → ConcurrentDictionary lock-free;
        //   getImplPtr 不需要(_map 直接存 MapEntry)。
        if (!_map.TryGetValue(key, out var entry))
        {
            return (null, null);
        }

        // cppcache mePtr->getValueI(value) 把 destroyed token 正規化成 null
        //   (MapEntryImpl::getValueI),接著的判斷再加 tombstone。我們 Value
        //   不做正規化,所以這裡顯式檢 destroyed + tombstone(+ null)= not found。
        //   注意:invalid token 不算 not found（cppcache getValueI 只折 destroyed,
        //   invalidated entry 會把 invalid token 當作「找到」回傳）。
        var value = entry.Value;
        if (value is null || CacheableToken.IsDestroyed(value) || CacheableToken.IsTombstone(value))
        {
            return (null, null);
        }

        return (entry, value);
    }

    /// <inheritdoc />
    public override object? GetFromDisk(object key, MapEntry entry) => throw new NotImplementedException();

    /// <inheritdoc />
    public override void Clear()
    {
        // cppcache ConcurrentEntriesMap::clear (ConcurrentEntriesMap.cpp:66-71):
        //   for (index < m_concurrency) m_segments[index].clear();
        //   m_size = 0;
        // The per-segment clear loop collapses onto the single backing store —
        // ConcurrentDictionary.Clear() does the BCL-side striped wipe.
        _map.Clear();

        // cppcache m_size = 0. _size is the logical (tombstone-excluding) count,
        // maintained via Interlocked by Put/Remove; reset it atomically to match.
        // Clearing drops everything (tombstones included), so a flat 0 is correct.
        Interlocked.Exchange(ref _size, 0);
    }

    /// <inheritdoc />
    public override (MapEntry Entry, object? OldValue, bool IsUpdate) Put(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null)
    {
        // cppcache ConcurrentEntriesMap::put + MapSegment::put 兩層攤平。
        // 能寫的直接寫,缺型別 / 欄位 / 配置的留 `// ` 待補形。

        // ── cppcache m_spinlock + rehash ─────────────────────────────────
        // std::lock_guard<...> lk(m_spinlock);             ─┐ ConcurrentDictionary
        // if (m_map.size()*75/100 > prime) rehash();        ─┘ 全吸掉,跳過。
        var isUpdate = false;
        MapEntry entry;
        object? oldValue = null;
        if (!_map.TryGetValue(key, out var existing))
        {
            // ── cppcache: 不存在 → putNoEntry (fresh entry + 加進 map) ─
            if (delta is not null)
            {
                // 沒對應 entry 卻收到 delta — caller (PutLocalAsync 的 InvalidDelta
                // catch block) 接這個訊號去做 GetNoThrowFullObjectAsync fallback。
                throw new GfErrTypeException(GfErrType.InvalidDelta);
            }

            var fresh = factory.NewEntry(key, newValue, updateCount, destroyTracker, versionTag);
            _map[key] = fresh;
            entry = fresh;                                             // ← writeback gap

            //_map[key] = new MapEntry();   // 最小占位 — walking-skeleton
        }
        else
        {
            // ── cppcache: 有 entry → 拿 meOldValue ──────────────────────
            var meOldValue = existing.Value;                           // MapEntry.Value 未實作

            // ── cppcache: m_concurrencyChecksEnabled → version tag 比對 ─
            // cppcache 在這先處理 versionStamp (ProcessVersionTag + SetVersions),
            //   tombstone 分支再帶舊 stamp 過去。C# 港因為 stamp 是 reference
            //   type,mutate existing.VersionStamp 之後,tombstone 分支直接從
            //   _map.TryRemove out 出的 entry 拿就好,不用 hoist。
            if (region.Attributes.ConcurrencyChecksEnabled)
            {
                var stamp = existing.VersionStamp;
                if (versionTag is not null)
                {
                    // ProcessVersionTag 失敗會拋 GfErrTypeException —
                    //   版本舊 → GfErrType.CacheConcurrentModificationException
                    //   race-loser → GfErrType.CacheEntryUpdated
                    // UpdateNoThrowAsync 的 catch switch 接這兩個分流。
                    stamp.ProcessVersionTag(region, key, versionTag, deltaCheck: delta is not null);
                    stamp.SetVersions(versionTag);
                }
            }

            // ── cppcache: tombstone resurrection ────────────────────────
            if (CacheableToken.IsTombstone(meOldValue))                 // CacheableToken / tombstone token 未實作
            {
                // cppcache MapSegment::remove_entry (L354-357) 是
                //   m_tombstoneList->erase(key) + m_map.erase(key) 兩步走;
                //   C# 對齊。region.TombstoneList 還沒 allocate,
                //   `?.` 短路,真實落地後才噴。
                region._tombstoneList?.Erase(key);
                // 拔掉 tombstone 並從它身上拿 (已經 ProcessVersionTag /
                //   SetVersions mutate 過的) stamp 帶到 fresh entry,history 延續。
                _map.TryRemove(key, out var tombstoned);
                var fresh = factory.NewEntry(key, newValue, updateCount, destroyTracker,
                    versionTag, carriedStamp: tombstoned?.VersionStamp);
                _map[key] = fresh;
                entry = fresh;                                          // ← writeback gap
                oldValue = null;
                isUpdate = false;
            }
            else
            {
                // putForTrackedEntry — tracked-update path (in-place mutate `existing`)
                // cppcache 在 putForTrackedEntry 用 `updateCount < 0 ||
                //   m_concurrencyChecksEnabled` 切兩路;C# 鏡像:
                //   * 兩條件都不成立 → 「tracked put + 需要 race detection」→
                //     PutForTrackedEntry (tracker branches still NIE)
                //   * concurrencyChecksEnabled → 寫成功後需要 tombstone-list erase
                //     → 也走 PutForTrackedEntry
                //   * 其餘 (預設:updateCount < 0,checks off) →
                //     走下面 inline happy path。
                if (updateCount >= 0 || region.Attributes.ConcurrencyChecksEnabled)
                {
                    PutForTrackedEntry(existing, key, newValue, updateCount, delta);
                }
                else if (delta is not null)
                {
                    // cppcache MapSegment::putForTrackedEntry delta path
                    //   (MapSegment.cpp:616-668)
                    var existingValue = existing.Value;

                    // 沒可套 delta 的「值」(空 / destroyed / invalid / tombstone)
                    //   → 退回 full object 抓回
                    if (existingValue is null
                        || CacheableToken.IsDestroyed(existingValue)
                        || CacheableToken.IsInvalid(existingValue)
                        || CacheableToken.IsTombstone(existingValue))
                    {
                        (region.Pool as ThinClientPoolDM)?.UpdateNotificationStats(false, TimeSpan.Zero);
                        // cppcache: m_poolDM->updateNotificationStats(false, 0)
                        //   wire 通知統計待加。
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }

                    // overflow 到磁碟 → 回讀,撈不回視為 InvalidDelta
                    if (CacheableToken.IsOverflowed(existingValue))
                    {
                        existingValue = GetFromDisk(key, existing);
                        if (existingValue is null)
                        {
                            (region.Pool as ThinClientPoolDM)?.UpdateNotificationStats(false, TimeSpan.Zero);
                            throw new GfErrTypeException(GfErrType.InvalidDelta);
                        }
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
        return (entry, oldValue, isUpdate);
    }

    /// <inheritdoc />
    public override (MapEntry? Entry, object? OldValue) Remove(
        object key,
        int updateCount,
        VersionTag? versionTag,
        bool afterRemote)
    {
        // cppcache ConcurrentEntriesMap::remove + MapSegment::remove 兩層攤平。
        // ConcurrentEntriesMap dispatches by key hash; MapSegment 做真實工作。
        // .NET ConcurrentDictionary 已內建分段鎖,兩層收成一層。
        // cppcache `if (isEntryFound) --m_size` 收掉 — _map.Count 自帶。

        // ── cppcache MapSegment::remove L312-321 — concurrency-checks branch ─
        if (region.Attributes.ConcurrencyChecksEnabled)
        {
            return RemoveWhenConcurrencyEnabled(key, updateCount, versionTag, afterRemote);
        }

        // ── cppcache L323-337 — happy path (no concurrency-checks) ─────────
        // m_spinlock + m_map.find + m_map.erase 三步,ConcurrentDictionary.TryRemove 一次完成。
        if (!_map.TryRemove(key, out var entry))
        {
            // cppcache: didn't unbind, probably no entry...
            //   destroyTrackers > 0 → m_destroyedKeys[key] = destroyTrackers + 1
            //   destroy-tracker bookkeeping 還沒做,跳過。
            // cppcache returns GF_CACHE_ENTRY_NOT_FOUND; 對應 (null, null)。
            return (null, null);
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
        return (oldValue is null ? null : entry, oldValue);
    }

    /// <inheritdoc />
    public override void RemoveTrackerForEntry(object key)
    {
        throw new NotImplementedException(
            "ConcurrentEntriesMap.RemoveTrackerForEntry: pending tracker subsystem.");
        // cppcache 三層攤平到這個 method:
        //   ConcurrentEntriesMap::removeTrackerForEntry(key) →
        //     MapSegment::removeTrackerForEntry(key) →
        //       MapSegment::removeTrackerForEntry(key, entry, entryImpl)  ← 真實 body
        // .NET ConcurrentDictionary 已內建分段鎖,中層 dispatch 收成這裡。
        //
        // ── cppcache ConcurrentEntriesMap::removeTrackerForEntry (ConcurrentEntriesMap.cpp:195-201):
        //
        //   void ConcurrentEntriesMap::removeTrackerForEntry(
        //       const std::shared_ptr<CacheableKey>& key) {
        //     // This function is disabled if concurrency checks are enabled. The versioning
        //     // changes takes care of the version and no need for tracking the entry
        //     if (m_concurrencyChecksEnabled) return;
        //     segmentFor(key)->removeTrackerForEntry(key);
        //   }
        //
        // ── cppcache MapSegment::removeTrackerForEntry(key) (MapSegment.cpp:540-551):
        //
        //   void MapSegment::removeTrackerForEntry(
        //       const std::shared_ptr<CacheableKey>& key) {
        //     if (m_concurrencyChecksEnabled) return;
        //     std::lock_guard<decltype(m_spinlock)> lk(m_spinlock);
        //
        //     const auto& find = m_map.find(key);
        //     if (find != m_map.end()) {
        //       auto& entry = find->second;
        //       auto impl = entry->getImplPtr();
        //       removeTrackerForEntry(key, entry, impl);
        //     }
        //   }
        //
        // ── cppcache MapSegment::removeTrackerForEntry(key, entry, entryImpl) (MapSegment.hpp:99-123):
        //
        //   // remove a tracker for the given entry
        //   inline void removeTrackerForEntry(const std::shared_ptr<CacheableKey>& key,
        //                                     std::shared_ptr<MapEntry>& entry,
        //                                     std::shared_ptr<MapEntryImpl>& entryImpl) {
        //     // This function is disabled if concurrency checks are enabled. The
        //     // versioning
        //     // changes takes care of the version and no need for tracking the entry
        //     if (m_concurrencyChecksEnabled) return;
        //     std::pair<bool, int> trackerPair = entry->removeTracker();
        //     if (trackerPair.second <= 0) {
        //       std::shared_ptr<Cacheable> value;
        //       if (entryImpl == nullptr) {
        //         entryImpl = entry->getImplPtr();
        //       }
        //       entryImpl->getValueI(value);
        //       if (value == nullptr) {
        //         // get rid of an entry marked as destroyed
        //         m_map.erase(key);
        //         return;
        //       }
        //     }
        //     if (trackerPair.first) {
        //       entry = entryImpl ? entryImpl : entry->getImplPtr();
        //       m_map[key] = entry;
        //     }
        //   }
        //
        // 翻譯前的注意:
        //   - 第一/二層的 `if (m_concurrencyChecksEnabled) return;` early-out
        //     合進這層:if (region.Attributes.ConcurrencyChecksEnabled) return;
        //   - entry->removeTracker() → MapEntry.RemoveTracker (NIE) 回 (bool, int);
        //     需在 MapEntry 上 NIE 新增。
        //   - trackerPair.second <= 0 + value == null → `_map.TryRemove(key, out _)`
        //     (對映 `m_map.erase(key)`)。
        //   - trackerPair.first 真實作 → 把 entry 換成 entryImpl 後寫回 `_map[key]`
        //     。在 C# `MapEntryImpl` 已收成 `MapEntry`,這條重新指派可能可以省掉。
    }

}
