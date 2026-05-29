namespace Geode.Client.Internal;

/// <summary>
/// Per-key entry held inside <see cref="EntriesMap"/>: value plus
/// version / tracker / tombstone metadata. Mirrors cppcache
/// <c>MapEntryImpl</c> (<c>cppcache/src/MapEntry.hpp</c>). Skeleton only
/// — members land with the caching-enabled phase (Phase 2+).
/// </summary>
internal sealed class MapEntry
{
    /// <summary>
    /// Whether this entry already has an entry-expiry task scheduled.
    /// Mirrors cppcache
    /// <c>MapEntryImpl::getExpProperties().task_scheduled()</c>
    /// (<c>cppcache/src/MapEntry.hpp</c>, <c>ExpProperties</c> chain).
    /// Flat property here for Phase 2+ NIE convenience; cppcache's
    /// two-step <c>ExpProperties</c> accessor will be reintroduced when
    /// the full expiry surface lands.
    /// </summary>
    public bool IsExpiryTaskScheduled => throw new NotImplementedException(
        "MapEntry.IsExpiryTaskScheduled: pending Phase 2+ expiry plumbing.");

    /// <summary>
    /// Per-entry concurrency-check version stamp. Mirrors cppcache
    /// <c>MapEntryImpl::getVersionStamp()</c>
    /// (<c>cppcache/src/MapEntry.hpp:59</c>, pure virtual,
    /// <c>VersionStamp&amp;</c> by-reference for in-place mutation).
    /// Phase 2+ concurrency-checks feature 落地後,
    /// <see cref="VersionStamp"/> 才會帶 fields + <c>ProcessVersionTag</c>
    /// / <c>SetVersions</c> 方法。
    /// </summary>
    public VersionStamp VersionStamp => throw new NotImplementedException(
        "MapEntry.VersionStamp: pending Phase 2+ concurrency-checks plumbing.");

    /// <summary>
    /// Current entry value (<see langword="null"/> for
    /// tombstone / deleted-but-not-collected entries). Mirrors cppcache
    /// <c>MapEntry::getValue</c> / <c>setValue</c>
    /// (<c>cppcache/src/MapEntry.hpp:53-54</c>, pure virtual) — the two
    /// methods collapse to a single C# property; out-param
    /// (<c>shared_ptr&amp;</c>) → return value, setter handles the
    /// inbound shape.
    /// </summary>
    public object? Value
    {
        get => throw new NotImplementedException(
            "MapEntry.Value getter: pending Phase 2+ caching plumbing.");
        set => throw new NotImplementedException(
            "MapEntry.Value setter: pending Phase 2+ caching plumbing.");
    }
}
