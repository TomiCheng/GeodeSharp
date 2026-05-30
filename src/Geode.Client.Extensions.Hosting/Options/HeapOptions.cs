namespace Geode.Client.Options;

/// <summary>
/// Heap-LRU and tombstone settings mirrored from cppcache
/// <c>SystemProperties</c>. These are server-side cache-control concepts
/// that cppcache surfaces to the client; on the .NET side they are very
/// likely no-ops and on the deletion shortlist.
/// </summary>
public class HeapOptions : ICloneable
{
    public HeapOptions() { }

    public HeapOptions(HeapOptions other)
    {
        LRULimit = other.LRULimit;
        LRUDelta = other.LRUDelta;
        TombstoneTimeout = other.TombstoneTimeout;
    }

    /// <summary>
    /// Heap-size threshold in megabytes that triggers LRU eviction.
    /// Mirrors cppcache <c>heap-lru-limit</c>; default 0 (= disabled).
    /// </summary>
    public ulong LRULimit { get; set; }

    /// <summary>
    /// Percentage of entries evicted in one LRU pass. Mirrors cppcache
    /// <c>heap-lru-delta</c>; default 10.
    /// </summary>
    public int LRUDelta { get; set; } = 10;

    /// <summary>
    /// How long a tombstone (deleted-entry marker) is retained before
    /// the server reclaims it. Mirrors cppcache <c>tombstone-timeout</c>;
    /// default 480 seconds.
    /// </summary>
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromSeconds(480);

    /// <summary>Deep clone via copy constructor.</summary>
    public HeapOptions Clone() => new(this);
    object ICloneable.Clone() => Clone();

    /// <summary>Validate this section. No structural rules currently — parity stub.</summary>
    public IEnumerable<string> Validate(string prefix)
    {
        yield break;
    }
}
