namespace Geode.Client.Internal.Entry;

internal interface IMapEntry
{
    object Key { get; }
    object? Value { get; set; }
    int UpdateCount { get; }
    void Cleanup(CacheEventFlags eventFlags) { }
    void IncrementUpdateCount(IMapEntry newEntry);

    /// <summary>
    /// Start tracking this entry; returns its current update sequence number.
    /// cppcache <c>MapEntry::addTracker</c> (<c>MapEntry.hpp:80</c>) — the
    /// <c>shared_ptr&amp;newEntry</c> morph out-param is kept as a plain
    /// (currently rebind-less) arg, like <see cref="IncrementUpdateCount"/>.
    /// </summary>
    int AddTracker(IMapEntry newEntry);

    /// <summary>
    /// Stop tracking this entry. Returns (shouldReplace-with-plain-MapEntryImpl,
    /// remaining-tracker-count). cppcache <c>MapEntry::removeTracker</c>
    /// (<c>MapEntry.hpp:89</c>, <c>std::pair&lt;bool,int&gt;</c> → C# tuple).
    /// </summary>
    (bool ShouldReplace, int Trackers) RemoveTracker();

    /// <summary>
    /// Current tracking number; <c>0</c> = not being tracked. cppcache
    /// <c>MapEntry::getTrackingNumber</c> (<c>MapEntry.hpp:105</c>).
    /// </summary>
    int TrackingNumber { get; }
}
