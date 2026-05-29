using System.Collections.Concurrent;
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
/// region back-ref, expiry manager) which arrive Phase 2+.
/// </remarks>
internal class ConcurrentEntriesMap(IServiceProvider serviceProvider,
    EntryFactory factory, bool concurrencyChecksEnabled, LocalRegion region, int concurrency)
    : EntriesMap
{
    IServiceProvider _ = serviceProvider;
    LocalRegion _0 = region;

    static readonly ObjectFactory<ConcurrentEntriesMap> _objectFactory
        = ActivatorUtilities.CreateFactory<ConcurrentEntriesMap>(
            [typeof(EntryFactory), typeof(bool), typeof(LocalRegion), typeof(int)]);

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
    /// Backing store. Replaces cppcache <c>MapSegment[] m_segments</c>
    /// + per-segment <c>unordered_map</c> + manual lock striping.
    /// </summary>
    // TODO Phase 2+: ctor will take (concurrency, initialCapacity) from
    //   RegionAttributes once EntriesMapFactory's plain branch is wired.
    //   Default-construct for now so Count works against an empty map.
    private readonly ConcurrentDictionary<object, MapEntry> _map = new();

    /// <summary>
    /// Mirrors cppcache <c>ConcurrentEntriesMap::size()</c>
    /// (<c>cppcache/src/ConcurrentEntriesMap.cpp</c>) — sums per-segment
    /// counts. Here it's a direct read of the dictionary count.
    /// </summary>
    internal override int Count => _map.Count;

    /// <inheritdoc />
    public override object? GetFromDisk(object key, MapEntry entry) => throw new NotImplementedException();

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

            //_map[key] = new MapEntry();   // 最小占位 — Phase 1.x 走 walking-skeleton
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
                //   C# 對齊。Phase 1.x region.TombstoneList 還沒 allocate,
                //   `?.` 短路,Phase 2+ 落地後真噴。
                region.TombstoneList?.Erase(key);
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
                //     PutForTrackedEntry (Phase 2+ NIE)
                //   * concurrencyChecksEnabled → 寫成功後需要 tombstone-list erase
                //     → 也走 PutForTrackedEntry
                //   * 其餘 (Phase 1.x default:updateCount < 0,checks off) →
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
                        (region.Pool as ThinClientPoolDM)?.UpdateNotificationStats(false, 0);
                        // cppcache: m_poolDM->updateNotificationStats(false, 0)
                        //   wire 通知統計 Phase 4+。
                        throw new GfErrTypeException(GfErrType.InvalidDelta);
                    }

                    // overflow 到磁碟 → 回讀,撈不回視為 InvalidDelta
                    if (CacheableToken.IsOverflowed(existingValue))
                    {
                        existingValue = GetFromDisk(key, existing);
                        if (existingValue is null)
                        {
                            (region.Pool as ThinClientPoolDM)?.UpdateNotificationStats(false, 0);
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
                            tempVal.FromDelta(delta);
                            existing.Value = tempVal;
                        }
                        else
                        {
                            // in-place path:直接 mutate 原物
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
                // Writeback 對兩條 branch 都一致:Phase 2+ PutForTrackedEntry
                //   返回成功時也是這套 entry / oldValue / isUpdate (跟 inline
                //   path 同款),所以擺在 if/else if/else 外。
                entry = existing;
                oldValue = meOldValue;
                isUpdate = meOldValue is not null;
            }
        }

        return (entry, oldValue, isUpdate);
        // ── cppcache `if (!isUpdate) ++m_size;` 收掉 ───────────────────
        // _map.Count 自帶,不用手動 bookkeeping。
    }

    /// <summary>
    /// Tracked-entry update path. Mirrors cppcache
    /// <c>MapSegment::putForTrackedEntry</c>
    /// (<c>cppcache/src/MapSegment.cpp:601-668</c>) — MapSegment collapse
    /// 後落腳在這。Phase 1.x 的 happy path (no tracker, no concurrency-
    /// checks, no tombstone-list) 目前直接 inline 在 <see cref="Put"/> 的
    /// tracked-update <c>else</c> 分支裡;這個 method 是 Phase 2+ 真接
    /// 時的歸宿 + 兩個待補行為的明確落腳點。
    /// </summary>
    /// <remarks>
    /// Phase 2+ 要補的行為:
    /// <list type="number">
    /// <item><description>
    /// <b>destroyTracker / updateCount race detection</b> — cppcache
    /// L606 對 <c>updateCount &lt; 0 || m_concurrencyChecksEnabled</c>
    /// 切「非 tracked put」vs「tracked put」兩條 path。Tracked put 比對
    /// caller (在 <c>AddTrackerForEntry</c> 記下) 的 <c>updateCount</c>
    /// 跟 entry 現在的 counter,不一致 → throw
    /// <see cref="GfErrTypeException"/>(<see cref="GfErrType.CacheEntryUpdated"/>);
    /// <c>UpdateNoThrowAsync</c> 的 catch switch 已在等這個訊號。
    /// </description></item>
    /// <item><description>
    /// <b>m_tombstoneList-&gt;erase(key, true)</b> — cppcache L673-674
    /// 在 concurrency-checks 開啟且寫成功時,把 tombstone list 裡同 key
    /// 的殘留掃掉。需 <c>LocalRegion.TombstoneList</c> 真實型別(目前
    /// 是 <see langword="object"/>?placeholder)。
    /// </description></item>
    /// </list>
    /// </remarks>
    private void PutForTrackedEntry(
        MapEntry existing,
        object key,
        object newValue,
        int updateCount,
        DataInput? delta) =>
        throw new NotImplementedException(
            "ConcurrentEntriesMap.PutForTrackedEntry: Phase 2+ extraction target (updateCount race detection + tombstone-list erase).");
}
