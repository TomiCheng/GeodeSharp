using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Cache-wide heap-LRU coordinator: a background service that tracks the
/// summed heap usage reported by all heap-LRU regions and triggers
/// cross-region eviction once the total exceeds the configured limit.
/// Mirrors cppcache <c>EvictionController</c>
/// (<c>cppcache/src/EvictionController.hpp:63</c>).
/// </summary>
/// <remarks>
/// Internal machinery (not a user extension point). Heap-LRU only —
/// entry-count LRU never touches it; no consumer until heap LRU lands
/// (see NOTE.md "Heap-LRU entry sizing"). Skeleton: all methods NIE,
/// fields mirror cppcache 1:1 (std::thread → <see cref="Task"/>,
/// condition_variable / shared_mutex park as placeholders).
/// </remarks>
internal sealed class EvictionController(
    SystemProperties systemProperties,
    ILogger<EvictionController> logger)
{
    private readonly ILogger<EvictionController> _logger = logger;


    // cppcache `cache_` (CacheImpl* back-ref):刻意不 port —
    //   cppcache 用它在 evict() 內 `cache_->getRegion(name)` 把 name 換成
    //   RegionInternal*。我們 _regions 直接存 RegionInternal ref(C# GC 不
    //   像 shared_ptr 怕循環持有,只要 dispose 紀律可靠就好),Evict 一步直通,
    //   完全不需要 cache back-ref。

    /// <summary>cppcache <c>thread_</c> (<c>std::thread</c>): the background service loop.</summary>
    private Task? _thread;

    /// <summary>cppcache <c>running_</c> (<c>std::atomic&lt;bool&gt;</c>): service-loop run flag.</summary>
    private bool _running;

    /// <summary>
    /// cppcache <c>max_heap_size_</c>: heap-size cap in <b>bytes</b>.
    /// <see cref="SystemProperties.HeapLRULimit"/> 是 MB,ctor 內 <c>&lt;&lt; 20</c>(×1048576)轉 bytes,
    /// 對齊 cppcache <c>EvictionController.cpp:43</c> 的 <c>max_heap_size &lt;&lt; 20ULL</c>。
    /// </summary>
    private readonly long _maxHeapSize = systemProperties.HeapLRULimit << 20;

    /// <summary>
    /// cppcache <c>heap_size_delta_</c>: per-eviction over-evict 緩衝,fraction(<c>0.10f</c> = 10%)。
    /// <see cref="SystemProperties.HeapLRUDelta"/> 是整數百分比,ctor 內 <c>/ 100.0f</c> 預轉成 fraction,
    /// 對齊 cppcache <c>EvictionController.cpp:44</c> 的 <c>heap_size_delta / 100.0f</c>。
    /// </summary>
    private readonly float _heapSizeDelta = systemProperties.HeapLRUDelta / 100.0f;

    /// <summary>cppcache <c>heap_size_</c> (<c>std::atomic&lt;int64_t&gt;</c>): current summed heap size across registered regions.</summary>
    private long _heapSize;

    /// <summary>
    /// cppcache <c>cv_</c> (<c>std::condition_variable</c>): wakes the
    /// service loop. <see cref="SemaphoreSlim"/>(<c>initialCount: 0</c>,
    /// 無 maxCount)當對應 — <c>Release</c> 對齊 <c>notify_one</c>,
    /// <c>WaitAsync</c> 對齊 <c>wait</c>(async-friendly,Svc 等待時不佔
    /// thread)。多次 Release 累積,搭 Svc 醒來後手動 re-check predicate
    /// 等價 cppcache spurious-wake 保護。
    /// </summary>
    private readonly SemaphoreSlim _signal = new(initialCount: 0);

    /// <summary>
    /// cppcache <c>regions_</c> (<c>std::set&lt;std::string&gt;</c>) 對應 — 但**刻意改存
    /// <see cref="RegionInternal"/> ref** 而非 name。cppcache 存 name 是為了避免
    /// <c>shared_ptr</c> 延長 region 生命週期 + 透過 <c>cache_-&gt;getRegion()</c> 自帶 null
    /// 檢查 race;C# 走 GC + 明確 Dispose,LRUEntriesMap.DisposeAsync 一定會
    /// UnregisterRegion,沒有 shared_ptr 循環持有風險。改存 ref 後 Evict 不用
    /// 經 cache lookup,直接迭代呼叫 region.EvictAsync。
    /// </summary>
    private readonly HashSet<RegionInternal> _regions = [];

    /// <summary>cppcache <c>regions_mutex_</c> (<c>boost::shared_mutex</c>): guards <see cref="_regions"/>.</summary>
    private readonly object _regionsLock = new();

    /// <summary>Start the background service loop. cppcache <c>start</c> (<c>EvictionController.hpp:70</c>).</summary>
    public void Start()
    {
        // cppcache EvictionController::start (EvictionController.cpp:49-54).
        // cppcache `std::thread(&svc, this)` → `Task.Run(SvcAsync)`,Svc 本身 async,
        //   等待時不佔 thread pool worker。
        // Idempotent guard 補 cppcache 沒有的 — IAsyncDisposable cascade / 重入呼叫
        //   都會安全 noop。
        if (Volatile.Read(ref _running)) return;
        Volatile.Write(ref _running, true);
        _thread = Task.Run(SvcAsync);
        _logger.LogDebug("Eviction Controller started");
    }

    /// <summary>
    /// Stop the background service loop and clear the region registry.
    /// cppcache <c>stop</c> (<c>EvictionController.hpp:72</c>) —
    /// <c>thread_.join()</c> 是 sync,我們 await Task,所以 async-first
    /// 改名 <c>StopAsync</c>。
    /// </summary>
    public async Task StopAsync()
    {
        // cppcache EvictionController::stop (EvictionController.cpp:56-63).
        // Idempotent guard 補 cppcache 沒有的 — 第二次呼叫 noop。
        if (!Volatile.Read(ref _running)) return;
        Volatile.Write(ref _running, false);
        // 戳醒 SvcAsync,讓它 await 醒來看到 _running=false 退 loop。
        _signal.Release();
        if (_thread is not null)
        {
            await _thread.ConfigureAwait(false);
        }
        lock (_regionsLock)
        {
            _regions.Clear();
        }
        _logger.LogDebug("Eviction controller stopped");
    }

    /// <summary>
    /// Background loop body — waits for heap-size signals and runs
    /// <see cref="CheckHeapSizeAsync"/>。cppcache <c>svc</c>
    /// (<c>EvictionController.hpp:74</c>) 是 <c>public:</c>(因為
    /// <c>std::thread(&amp;svc, this)</c> 需要可呼叫);C# <c>Task.Run</c>
    /// 在 class 內部不挑訪問權,改 private 收掉外部 surface,只剩
    /// <see cref="Start"/> 自己 spawn。
    /// </summary>
    private async Task SvcAsync()
    {
        // cppcache EvictionController::svc (EvictionController.cpp:65-78).
        // cppcache `cv.wait(lock, predicate)` 的 predicate 是
        //   `!running_ || heap_size_ > max_heap_size_`,wait 內部 loop 自動
        //   重檢處理 spurious wake。對應到 SemaphoreSlim 模型:
        //   * Wake 機制 — IncrementHeapSize / StopAsync 都 Release。
        //   * Predicate 改成 wake 後手動 if 一輪 —
        //       !_running → 直接 break;
        //       否則 → CheckHeapSize 自己會 short-circuit「<= max」的情況。
        //   * 多次 Release 累積,WaitAsync 跑多輪 — 每輪 CheckHeapSize 早退
        //     (harmless),等價 cppcache spurious wake。
        // cppcache `Log::setThreadName("NC EC Thread")` 跳過 — Task pool
        //   沒 stable per-Task thread name,Activity tag 是另一條工具。
        while (Volatile.Read(ref _running))
        {
            await _signal.WaitAsync().ConfigureAwait(false);
            if (!Volatile.Read(ref _running)) break;
            await CheckHeapSizeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Evict <paramref name="percentage"/> of entries across registered
    /// regions. cppcache <c>evict</c> (<c>EvictionController.hpp:76</c>) —
    /// cppcache 是 <c>public:</c>(預留 force-trigger admin 路);我們無 class
    /// 外 caller(唯一進入點是 <see cref="CheckHeapSizeAsync"/>),降 private,
    /// 真有外部觸發需求再升。Sync 改 async,下游 <see cref="RegionInternal.EvictAsync"/>
    /// 已 Task。
    /// </summary>
    private async Task EvictAsync(float percentage, CancellationToken ct = default)
    {
        // cppcache EvictionController::evict (EvictionController.cpp:122-145).
        // Snapshot 邏輯保留 — 鎖只圈起來複製 region list,然後立刻釋放,
        //   per-region EvictAsync 在鎖外跑,不阻擋 Register/Unregister。
        // cppcache 用 `dynamic_pointer_cast<RegionInternal>(cache_->getRegion(name))`,
        //   我們 _regions 直接存 RegionInternal,完全跳過 cache lookup + cast。
        // 順序處理(非 Task.WhenAll)對齊 cppcache 序列化語意 — Svc loop
        //   等所有 region evict 完才回去 await 下次 signal。
        // Race:region 在 snapshot 跟實際 EvictAsync 之間被 Dispose,LocalRegion.EvictAsync
        //   第一行檢查 _released / _destroyPending 自動 noop,callee 端處理。
        List<RegionInternal> snapshot;
        lock (_regionsLock)
        {
            snapshot = new List<RegionInternal>(_regions);
        }

        foreach (var region in snapshot)
        {
            await region.EvictAsync(percentage, ct).ConfigureAwait(false);
        }
    }

    /// <summary>A region reports its heap-size delta (called from <c>LRUEntriesMap.updateMapSize</c>). cppcache <c>incrementHeapSize</c> (<c>EvictionController.hpp:77</c>).</summary>
    public void IncrementHeapSize(long delta)
    {
        // cppcache EvictionController::incrementHeapSize (EvictionController.cpp:80-86).
        // Atomic add(64-bit 對齊不是所有 platform 都保證,Interlocked.Add 安全)+
        // 戳醒 SvcAsync。cppcache 註解的「could block here to prevent further
        // memory use until evictions complete」設計沒接,跟 cppcache 行為一致
        // (純 fire-and-forget signal)。
        Interlocked.Add(ref _heapSize, delta);
        _signal.Release();
    }

    /// <summary>Register a heap-LRU region. cppcache <c>registerRegion(const string&amp; name)</c> (<c>EvictionController.hpp:78</c>) — 我們收 region ref 而非 name,避開 cache.GetRegion 回查。</summary>
    public void RegisterRegion(RegionInternal region)
    {
        // cppcache EvictionController::registerRegion (EvictionController.cpp:106-112).
        // boost::unique_lock 是 shared_mutex 的寫者鎖;C# 用 plain lock() 對應寫者一邊,
        // 等 Evict() 真有讀者競爭時再升 ReaderWriterLockSlim。
        // HashSet<T>.Add 回 bool(true = 新插入),對應 std::set::insert(...).second。
        // RegionInternal 用 reference equality(沒 override Equals),每個 region 實例
        // 唯一,語意對。
        lock (_regionsLock)
        {
            if (_regions.Add(region))
            {
                _logger.LogDebug(
                    "Registered region with Heap LRU eviction controller: name is {RegionName}",
                    region.FullPath);
            }
        }
    }

    /// <summary>Deregister a region (on destroy). cppcache <c>unregisterRegion(const string&amp; name)</c> (<c>EvictionController.hpp:79</c>) — 對齊 RegisterRegion 也改收 region ref。</summary>
    public void UnregisterRegion(RegionInternal region)
    {
        // cppcache EvictionController::unregisterRegion (EvictionController.cpp:114-120).
        // 跟 RegisterRegion 對稱:HashSet<T>.Remove 回 bool(true = 真的拔掉),
        // 對應 std::set::erase(...) > 0(回傳被擦掉的個數)。
        lock (_regionsLock)
        {
            if (_regions.Remove(region))
            {
                _logger.LogDebug(
                    "Deregistered region with Heap LRU eviction controller: name is {RegionName}",
                    region.FullPath);
            }
        }
    }

    /// <summary>Compare summed heap size to the limit and order eviction if over. cppcache <c>checkHeapSize</c> (<c>EvictionController.hpp:82</c>, private).</summary>
    private async Task CheckHeapSizeAsync(CancellationToken ct = default)
    {
        // cppcache EvictionController::checkHeapSize (EvictionController.cpp:88-104).
        // Snapshot atomic-once 對應 cppcache `int64_t heap_size = heap_size_;`
        //   — 後續計算 / log 全用快照,避免讀到中途被 IncrementHeapSize 改的值。
        //   Interlocked.Read 保證 64-bit 原子讀(32-bit OS 也安全)。
        // `_heapSizeDelta` ctor 已預先 `/ 100.0f` 轉 fraction,這裡直接 `+ _heapSizeDelta`。
        // LOGFINE → LogDebug + structured params(%.03f → :F3 保留三位小數)。
        // Evict 目前還是 NIE,但 Svc / IncrementHeapSize 也是 NIE,實際上不會走進來。
        var heapSize = Interlocked.Read(ref _heapSize);
        if (heapSize <= _maxHeapSize)
        {
            return;
        }

        var percentage = (float)(heapSize - _maxHeapSize) / _maxHeapSize + _heapSizeDelta;

        _logger.LogDebug(
            "EvictionController::process_delta: evicting {Percentage:F3}% of the entries. Heap size is: {HeapSize} / {MaxHeapSize}",
            percentage * 100.0f, heapSize, _maxHeapSize);

        await EvictAsync(percentage, ct).ConfigureAwait(false);
    }
}
