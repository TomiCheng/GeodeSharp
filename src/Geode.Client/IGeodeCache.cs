namespace Geode.Client;

/// <summary>
/// A connection to a single Geode cluster. Obtained from
/// <see cref="IGeodeCacheFactory"/> (or, for the unnamed registration,
/// resolved directly from DI).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors cppcache <c>GeodeCache</c>
/// (<c>cppcache/include/geode/GeodeCache.hpp</c>), the middle tier of
/// the upstream <c>RegionService</c> &#x2192; <c>GeodeCache</c>
/// &#x2192; <c>Cache</c> hierarchy. Lifecycle and lookup methods live
/// on the base <see cref="IRegionService"/>; this interface adds
/// cache-instance-scoped surface (name, eager init, future PDX
/// configuration accessors).
/// </para>
/// <para>
/// We do not currently expose a separate "concrete cache" interface
/// equivalent to cppcache's <c>Cache</c> class &#x2014; methods that
/// live on <c>Cache</c> in cppcache (transaction manager, pool
/// manager, authenticated views, etc.) will be added either to this
/// interface or to a derived one as their phases ship.
/// </para>
/// </remarks>
public interface IGeodeCache : IRegionService
{
    /// <summary>
    /// Logical name this cache was registered under. Empty string for
    /// the unnamed default.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Open the connection and run the handshake if it has not been
    /// done yet. Idempotent: subsequent calls return the same
    /// completed <see cref="Task"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling this is <b>optional</b>. Region / query / ping
    /// operations on the cache will await it themselves on first use,
    /// so consumers typically never need to call it directly. Use it
    /// to pre-warm the connection during application startup so the
    /// first user-facing request doesn't pay the handshake latency.
    /// </para>
    /// <para>
    /// Concurrent first-callers all await the same in-flight init.
    /// The <paramref name="ct"/> of the <i>first</i> caller dictates
    /// cancellation for everyone awaiting that init &#x2014; pass a
    /// token you control if you care.
    /// </para>
    /// </remarks>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    /// <summary>
    /// OQL query factory. Mirrors cppcache
    /// <c>Cache::getQueryService()</c> / <c>getQueryService(poolName)</c>
    /// (<c>cppcache/include/geode/Cache.hpp</c>) collapsed into one
    /// method.
    /// </summary>
    /// <param name="poolName">
    /// Pool to source the query service from. <see langword="null"/>
    /// or empty selects <c>PoolManager.DefaultPool</c>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="poolName"/> is supplied but no pool with that
    /// name is registered.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No default pool exists (cache not initialised, or all pools
    /// destroyed).
    /// </exception>
    IQueryService GetQueryService(string? poolName = null);

    // Phase 2: bool PdxIgnoreUnreadFields { get; }
    // Phase 2: bool PdxReadSerialized   { get; }
}
