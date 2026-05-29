namespace Geode.Client.Internal;

/// <summary>
/// Per-entry LRU bookkeeping carried by an LRU-variant <see cref="MapEntry"/>:
/// the entry's position in the intrusive LRU list plus its overflow
/// persistence handle. Mirrors cppcache <c>LRUEntryProperties</c>
/// (<c>cppcache/src/LRUEntryProperties.hpp:35</c>).
/// </summary>
/// <remarks>
/// Skeleton only — read when LRU eviction / overflow land (Phase 2+ / Phase
/// 4). cppcache stores the LRU list node <i>intrusively</i> on the entry
/// (<c>iter_</c>); if the C# port instead keeps an external
/// <c>LinkedList</c> + key→node dict (see <see cref="LRUEntriesMap"/>), this
/// <see cref="Node"/> slot may be dropped — design decision deferred to the
/// eviction-impl step.
/// </remarks>
internal sealed class LRUEntryProperties
{
    /// <summary>
    /// Opaque overflow disk-location handle set by the persistence manager.
    /// Mirrors cppcache <c>persistence_info_</c>
    /// (<c>std::shared_ptr&lt;void&gt;</c>, type-erased → <see langword="object"/>?).
    /// </summary>
    public object? PersistenceInfo { get; set; }

    /// <summary>
    /// This entry's node in the intrusive LRU list. Mirrors cppcache
    /// <c>iter_</c> (<c>std::list&lt;…&gt;::iterator</c>); typed
    /// <see langword="object"/>? until the LRU list type is settled.
    /// </summary>
    public object? Node { get; set; }
}
