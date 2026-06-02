namespace Geode.Client.Internal.Entry;

/// <summary>
/// Plain (non-LRU / non-versioned / non-expiring) map entry. Mirrors cppcache
/// <c>MapEntryImpl</c> (<c>cppcache/src/MapEntryImpl.hpp:49</c>).
/// </summary>
/// <remarks>
/// cppcache encodes the tracking number / update count as <b>template
/// parameters</b> on <c>MapEntryT&lt;TBase, NUM_TRACKERS, UPDATE_COUNT&gt;</c>
/// and "morphs" the entry (placement-new to a new template instantiation) on
/// each add-tracker / increment — purely to avoid storing the two counters as
/// per-entry fields. C# has no const-template morph, so we just store them as
/// plain <see cref="int"/> fields and mutate in place. Consequence: the whole
/// <c>MapEntryT</c> / <c>MapEntryST</c> / <c>TrackedMapEntry</c> machinery is
/// not needed, and cppcache's <c>shared_ptr&lt;MapEntry&gt;&amp; newEntry</c>
/// rebind out-param stays permanently unused (we never morph/rebind).
/// </remarks>
internal class MapEntry(object key) : IMapEntry
{
    private int _trackingNumber;
    private int _updateCount;

    public object Key => key;
    public object? Value { get; set; }

    public int UpdateCount => _updateCount;

    /// <summary>cppcache <c>getTrackingNumber</c> (<c>MapEntry.hpp:105</c>); <c>0</c> = not tracked.</summary>
    public int TrackingNumber => _trackingNumber;

    /// <summary>
    /// cppcache <c>addTracker</c> (<c>MapEntry.hpp:80</c> + <c>MapEntryST::addTracker</c>):
    /// bump the tracker count, return the current update sequence number.
    /// <paramref name="newEntry"/> is cppcache's morph out-param — unused here
    /// (no rebind).
    /// </summary>
    public virtual int AddTracker(IMapEntry newEntry)
    {
        _trackingNumber++;
        return _updateCount;
    }

    /// <summary>
    /// cppcache <c>removeTracker</c> (<c>MapEntry.hpp:89</c> + <c>MapEntryST::removeTracker</c>):
    /// drop one tracker; when the count reaches zero the update sequence is
    /// reset too (cppcache <c>MapEntry.hpp:76-78</c>). Returns
    /// (should-replace-with-plain-entry, remaining-tracker-count) — the C# port
    /// never morphs, so <c>ShouldReplace</c> is always <see langword="false"/>.
    /// </summary>
    public virtual (bool ShouldReplace, int Trackers) RemoveTracker()
    {
        if (_trackingNumber > 0)
        {
            _trackingNumber--;
        }
        if (_trackingNumber == 0)
        {
            _updateCount = 0;
        }
        return (false, _trackingNumber);
    }

    /// <summary>
    /// cppcache <c>incrementUpdateCount</c> (<c>MapEntry.hpp:99</c>): bump the
    /// update sequence. <paramref name="newEntry"/> morph out-param unused.
    /// </summary>
    public virtual void IncrementUpdateCount(IMapEntry newEntry) => _updateCount++;
}
