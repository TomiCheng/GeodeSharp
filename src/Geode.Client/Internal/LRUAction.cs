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
}
