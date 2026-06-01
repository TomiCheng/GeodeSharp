using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract eviction-strategy base for LRU-bounded entry maps. Mirrors
/// cppcache <c>LRUAction</c> (<c>cppcache/src/LRUAction.hpp:40</c>).
/// Skeleton only — the strategy body + subclasses (LRUInvalidateAction,
/// LRUDestroyAction, LRUOverflowAction, …) land with the LRU subsystem
/// (Phase 2+). Today the nested <see cref="Action"/> discriminator is
/// all that's consumed (by <see cref="EntriesMapFactory"/>).
/// </summary>
internal abstract class LRUAction
{
    /// <summary>
    /// Discriminator for <see cref="LRUAction"/> subclass selection.
    /// Mirrors cppcache nested enum <c>LRUAction::Action</c>
    /// (<c>cppcache/src/LRUAction.hpp:57-73</c>). Overlaps the first
    /// five values with <see cref="Options.CacheExpirationAction"/>;
    /// kept separate because the trailing <see cref="OverflowToDisk"/>
    /// is LRU-only and the public expiration enum shouldn't carry it.
    /// </summary>
    internal enum Action
    {
        Invalidate = 0,
        LocalInvalidate,
        Destroy,
        LocalDestroy,
        InvalidAction,
        OverflowToDisk,
    }

    // cppcache LRUAction protected flags (LRUAction.hpp:42-45) — default
    //   false; subclass ctors flip the relevant ones. Read via overflows()
    //   etc. in LRUEntriesMap's evict path.
    /// <summary>cppcache <c>m_invalidates</c>: action clears the value, keeps the entry.</summary>
    public bool Invalidates { get; protected set; }

    /// <summary>cppcache <c>m_destroys</c>: action removes the entry.</summary>
    public bool Destroys { get; protected set; }

    /// <summary>cppcache <c>m_distributes</c>: action propagates to the server (non-local).</summary>
    public bool Distributes { get; protected set; }

    /// <summary>cppcache <c>m_overflows</c>: action spills the value to disk.</summary>
    public bool Overflows { get; protected set; }

    /// <summary>
    /// Perform the eviction action on <paramref name="entry"/>; returns
    /// <see langword="true"/> when done. Mirrors cppcache <c>evict</c>
    /// (<c>cppcache/src/LRUAction.hpp:82</c>, pure virtual) — cppcache 是
    /// sync,我們 async-first(OVERFLOW_TO_DISK 走 IPersistenceManager.WriteAsync,
    /// 其他 action 走 region.DestroyNoThrowAsync / InvalidateAsync,都是 async)。
    /// </summary>
    public abstract Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default);
    public static LRUAction NewLRUAction(IServiceProvider serviceProvider,
        Action action, LocalRegion region, LRUEntriesMap lRUEntriesMap)
    {
        return action switch
        {
            Action.Invalidate => ActivatorUtilities.CreateInstance<LRULocalInvalidateAction>(serviceProvider, region),
            Action.LocalDestroy => ActivatorUtilities.CreateInstance<LRULocalDestroyAction>(serviceProvider, region, lRUEntriesMap),
            Action.OverflowToDisk => ActivatorUtilities.CreateInstance<LRUOverFlowToDiskAction>(serviceProvider, region, lRUEntriesMap),
            Action.Destroy => ActivatorUtilities.CreateInstance<LRUDestroyAction>(serviceProvider, region),
            _ => throw new ArgumentException($"Unsupported LRU eviction action: {action}.", nameof(action)),
        };
    }

    /// <summary>This action's discriminator. Mirrors cppcache <c>getType</c> (<c>LRUAction.hpp:84</c>).</summary>
    public abstract Action ActionType { get; }
}

/// <summary>
/// Distributed-destroy eviction — removes the evicted entry and propagates
/// the destroy to the server. Mirrors cppcache <c>LRUDestroyAction</c>
/// (<c>cppcache/src/LRUAction.hpp:98-130</c>).
/// </summary>
internal sealed class LRUDestroyAction : LRUAction
{
    private readonly LocalRegion _region;
    private readonly ILogger<LRUDestroyAction> _logger;

    public LRUDestroyAction(LocalRegion region, ILogger<LRUDestroyAction> logger)
    {
        _region = region;
        _logger = logger;
        Destroys = true;
        Distributes = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.Destroy;

    /// <summary>
    /// cppcache <c>LRUDestroyAction::evict</c> (<c>LRUAction.hpp:111-125</c>):
    /// <c>region.DestroyNoThrowAsync(key, EVICTION)</c>(無 <c>LOCAL</c> →
    /// 會分散到 server,<c>m_distributes=true</c>),region 已銷毀則跳過。
    /// 跟 <see cref="LRULocalDestroyAction"/> 唯一差別:旗標少 <c>LOCAL</c>
    /// + 多一道 <see cref="LocalRegion.IsDestroyed"/> guard。
    /// </summary>
    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        // cppcache LRUDestroyAction::evict (LRUAction.hpp:111-125).
        var key = entry.Key;
        _logger.LogDebug("LRUDestroy: evicting entry with key [{Key}]", key);

        // cppcache `if (!m_regionPtr->isDestroyed())` — 區域已銷毀就跳過 destroy,
        //   err 維持 GF_NOERR → 回 true(對齊 cppcache return)。
        if (_region.IsDestroyed)
        {
            return true;
        }

        try
        {
            // EVICTION(無 LOCAL)→ 不是 local-only,走分散式 destroy。
            await _region.DestroyNoThrowAsync(
                key,
                callbackArgument: null,
                updateCount: -1,
                eventFlags: CacheEventFlags.Eviction,
                versionTag: null,
                ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (GeodeException)
        {
            // cppcache `err != GF_NOERR` 路徑。
            return false;
        }
    }
}

/// <summary>
/// Local-invalidate eviction — clears the value, keeps the entry. Mirrors
/// cppcache <c>LRULocalInvalidateAction</c>
/// (<c>cppcache/src/LRUAction.hpp:135-152</c>).
/// </summary>
internal sealed class LRULocalInvalidateAction : LRUAction
{
    private readonly LocalRegion _region;

    public LRULocalInvalidateAction(LocalRegion region)
    {
        _region = region;
        Invalidates = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.LocalInvalidate;

    /// <summary>
    /// cppcache out-of-line <c>LRULocalInvalidateAction::evict</c> — local
    /// invalidate of the entry's value. Pending the region invalidate path
    /// (Phase 2+).
    /// </summary>
    public override Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
        => throw new NotImplementedException(
            "LRULocalInvalidateAction.EvictAsync: pending region local-invalidate path.");
}

/// <summary>
/// Overflow-to-disk eviction — spills the value to disk via the persistence
/// manager, leaving an overflow token in memory. Mirrors cppcache
/// <c>LRUOverFlowToDiskAction</c>
/// (<c>cppcache/src/LRUAction.hpp:157-175</c>).
/// </summary>
internal sealed class LRUOverFlowToDiskAction : LRUAction
{
    private readonly LocalRegion _region;
    private readonly LRUEntriesMap _entriesMap;

    public LRUOverFlowToDiskAction(LocalRegion region, LRUEntriesMap entriesMap)
    {
        _region = region;
        _entriesMap = entriesMap;
        Overflows = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.OverflowToDisk;

    /// <summary>
    /// cppcache out-of-line <c>LRUOverFlowToDiskAction::evict</c> — write the
    /// value to disk + swap in <see cref="CacheableToken"/>.Overflowed.
    /// Pending the persistence manager (Phase 4).
    /// </summary>
    public override Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
        => throw new NotImplementedException(
            "LRUOverFlowToDiskAction.EvictAsync: pending Phase 4 persistence manager (overflow-to-disk).");
}

/// <summary>
/// Local-destroy eviction — removes the evicted entry locally only (no
/// distributed destroy). Mirrors cppcache <c>LRULocalDestroyAction</c>
/// (<c>cppcache/src/LRULocalDestroyAction.hpp:38</c>). Default eviction
/// action for <c>LocalEntryLru</c>.
/// </summary>
internal sealed class LRULocalDestroyAction : LRUAction
{
    private readonly LocalRegion _region;
    private readonly LRUEntriesMap _entriesMap;
    private readonly ILogger<LRULocalDestroyAction> _logger;

    public LRULocalDestroyAction(
        LocalRegion region,
        LRUEntriesMap entriesMap,
        ILogger<LRULocalDestroyAction> logger)
    {
        _region = region;
        _entriesMap = entriesMap;
        _logger = logger;
        // local destroy: removes the entry but does NOT distribute (no m_distributes).
        Destroys = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.LocalDestroy;

    /// <summary>
    /// cppcache <c>LRULocalDestroyAction::evict</c>
    /// (<c>cppcache/src/LRULocalDestroyAction.cpp:27-39</c>):
    /// <c>getKeyI</c> → <c>region.destroyNoThrow(key, EVICTION | LOCAL, …)</c>。
    /// 成功 (err == GF_NOERR) 回 true,失敗或 region 已 destroy 回 false。
    /// </summary>
    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        // cppcache LRULocalDestroyAction::evict (LRULocalDestroyAction.cpp:27-39).
        // cppcache `mePtr->getKeyI(keyPtr)` → C# `entry.Key`(MapEntryImpl ctor 收進)。
        // LOGDEBUG → LogDebug + structured。
        // err-code → exception:`DestroyNoThrowAsync` throw 視為 evict 失敗,
        //   try/catch 收 GeodeException 系列轉 false 給 caller。
        // versionTag = null:cppcache 是 out-param 起始 null,給 DestroyNoThrowAsync
        //   的 default 直接吃。
        // CacheEventFlags.Eviction | .Local — 對應 cppcache 旗標。
        // cppcache `if (!m_regionPtr->isDestroyed())` 對應 LocalRegion 自己會在
        //   DestroyActions 路徑檢查 _released / _destroyPending,這裡省略外層 guard。
        var key = entry.Key;
        _logger.LogDebug("LRULocalDestroy: evicting entry with key [{Key}]", key);

        try
        {
            await _region.DestroyNoThrowAsync(
                key,
                callbackArgument: null,
                updateCount: -1,
                eventFlags: CacheEventFlags.Eviction | CacheEventFlags.Local,
                versionTag: null,
                ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (GeodeException)
        {
            // 對應 cppcache `err != GF_NOERR` 路徑 — 不丟,通知 caller 沒成功。
            return false;
        }
    }
}
