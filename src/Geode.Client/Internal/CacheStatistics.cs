namespace Geode.Client.Internal;

/// <summary>
/// Per-region / per-entry last-access &amp; last-modified timestamps,
/// consumed by the idle / TTL expiry tasks. Mirrors cppcache
/// <c>CacheStatistics</c>
/// (<c>cppcache/include/geode/CacheStatistics.hpp:39</c>) — the object
/// returned by <c>Region::getStatistics()</c>. Distinct from
/// <see cref="CachePerfStatistics"/> (cache-wide perf counters); cppcache
/// keeps them separate (<c>m_cacheStatistics</c> vs
/// <c>m_cacheImpl-&gt;getCachePerfStats()</c>) and so do we.
/// </summary>
/// <remarks>
/// cppcache <c>setLastAccessedTime</c> / <c>setLastModifiedTime</c> are
/// <c>private</c> + <c>friend class LocalRegion</c>; in the C# port they
/// surface as <c>internal</c> methods called from
/// <see cref="LocalRegion.UpdateAccessAndModifiedTime"/>. Skeleton only —
/// the atomic timestamp fields + getters land with the expiry subsystem
/// (Phase 2+).
/// </remarks>
internal sealed class CacheStatistics
{
    /// <summary>
    /// Record the region's last-accessed time. Mirrors cppcache
    /// <c>CacheStatistics::setLastAccessedTime</c>
    /// (<c>cppcache/include/geode/CacheStatistics.hpp:104</c>).
    /// </summary>
    public void SetLastAccessedTime(long timestamp) => throw new NotImplementedException();

    /// <summary>
    /// Record the region's last-modified time. Mirrors cppcache
    /// <c>CacheStatistics::setLastModifiedTime</c>
    /// (<c>cppcache/include/geode/CacheStatistics.hpp:105</c>).
    /// </summary>
    public void SetLastModifiedTime(long timestamp) => throw new NotImplementedException();
}
