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
    /// (<c>cppcache/src/LRUAction.hpp:82</c>, pure virtual).
    /// </summary>
    public abstract bool Evict(MapEntry entry);
    public static LRUAction NewLRUAction(IServiceProvider serviceProvider,
        Action action, LocalRegion region, LRUEntriesMap lRUEntriesMap)
    {
        return action switch
        {
            Action.Invalidate => ActivatorUtilities.CreateInstance<LRULocalInvalidateAction>(serviceProvider, region),
            Action.LocalDestroy => ActivatorUtilities.CreateInstance<LRULocalDestroyAction>(serviceProvider, region, lRUEntriesMap),
            Action.OverflowToDisk => ActivatorUtilities.CreateInstance<LRUOverFlowToDiskAction>(serviceProvider, region, lRUEntriesMap),
            Action.Destroy => ActivatorUtilities.CreateInstance<LRUDestroyAction>(serviceProvider, region, lRUEntriesMap),
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

    public LRUDestroyAction(LocalRegion region)
    {
        _region = region;
        Destroys = true;
        Distributes = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.Destroy;

    /// <summary>
    /// cppcache: <c>m_regionPtr-&gt;destroyNoThrow(key, EVICTION, …)</c> when
    /// the region isn't already destroyed. Pending the region destroy path
    /// (Phase 2+); needs <c>MapEntry</c> key + <c>DestroyNoThrow</c>.
    /// </summary>
    public override bool Evict(MapEntry entry) => throw new NotImplementedException(
        "LRUDestroyAction.Evict: pending region DestroyNoThrow (EVICTION) path.");
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
    public override bool Evict(MapEntry entry) => throw new NotImplementedException(
        "LRULocalInvalidateAction.Evict: pending region local-invalidate path.");
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
    public override bool Evict(MapEntry entry) => throw new NotImplementedException(
        "LRUOverFlowToDiskAction.Evict: pending Phase 4 persistence manager (overflow-to-disk).");
}

/// <summary>
/// Local-destroy eviction — removes the evicted entry locally only (no
/// distributed destroy). Mirrors cppcache <c>LRULocalDestroyAction</c>
/// (<c>cppcache/src/LRULocalDestroyAction.hpp:38</c>). Default eviction
/// action for <c>LocalEntryLru</c>.
/// </summary>
internal sealed class LRULocalDestroyAction
    : LRUAction
{
    private readonly LocalRegion _region;
    private readonly LRUEntriesMap _entriesMap;

    public LRULocalDestroyAction(LocalRegion region, LRUEntriesMap entriesMap)
    {
        _region = region;
        _entriesMap = entriesMap;
        // local destroy: removes the entry but does NOT distribute (no m_distributes).
        Destroys = true;
    }

    /// <inheritdoc />
    public override Action ActionType => Action.LocalDestroy;

    /// <summary>
    /// cppcache <c>LRULocalDestroyAction::evict</c>
    /// (<c>cppcache/src/LRULocalDestroyAction.cpp</c>): <c>getKeyI</c> →
    /// <c>m_regionPtr-&gt;destroyNoThrow(key, EVICTION | LOCAL, …)</c>,
    /// returns <c>err == GF_NOERR</c>. Pending region <c>DestroyNoThrow</c>
    /// + <c>MapEntry.Key</c>.
    /// </summary>
    public override bool Evict(MapEntry entry)
    {
        throw new NotImplementedException(
            "LRULocalDestroyAction.Evict: pending region DestroyNoThrow (EVICTION | LOCAL) + MapEntry.Key.");
        // cppcache LRULocalDestroyAction::evict (LRULocalDestroyAction.cpp:27-39):
        //
        //   bool LRULocalDestroyAction::evict(const std::shared_ptr<MapEntryImpl>& mePtr) {
        //     std::shared_ptr<CacheableKey> keyPtr;
        //     mePtr->getKeyI(keyPtr);
        //     std::shared_ptr<VersionTag> versionTag;
        //     //  we should invoke the destroyNoThrow with appropriate
        //     // flags to correctly invoke listeners
        //     LOGDEBUG("LRULocalDestroy: evicting entry with key [%s]",
        //              Utils::nullSafeToString(keyPtr).c_str());
        //     GfErrType err = m_regionPtr->destroyNoThrow(
        //         keyPtr, nullptr, -1, CacheEventFlags::EVICTION | CacheEventFlags::LOCAL,
        //         versionTag);
        //     return (err == GF_NOERR);
        //   }
    }
}
