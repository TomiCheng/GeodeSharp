namespace Geode.Client;

/// <summary>
/// Common region / query lookup contract. Mirrors cppcache
/// <c>RegionService</c> (<c>cppcache/include/geode/RegionService.hpp</c>),
/// the top of the three-tier <c>RegionService</c> &#x2192;
/// <c>GeodeCache</c> &#x2192; <c>Cache</c> hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by <see cref="IGeodeCache"/> for full-cache scope. In
/// Phase 3 (multi-user security) an <c>IAuthenticatedView</c> sibling
/// is expected to also implement this interface to expose a per-user
/// view of the same cache &#x2014; cppcache mirrors that arrangement
/// with <c>AuthenticatedView : RegionService</c>.
/// </para>
/// <para>
/// Region lookup, query service, and PDX instance factory accessors
/// will land on this interface as their respective phases ship
/// (Phase 1.2 / 1.4 / 2). Today it is the lifecycle surface only.
/// </para>
/// </remarks>
public interface IRegionService : IAsyncDisposable
{
    /// <summary>Whether <see cref="CloseAsync"/> has been called.</summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gracefully close the underlying connection(s). Subsequent calls
    /// are a no-op.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>
    /// Get the strongly-typed handle for the region at
    /// <paramref name="path"/>. Returns <c>null</c> when no region
    /// with that path is registered. Mirrors cppcache
    /// <c>RegionService::getRegion(const std::string&amp; path)</c>
    /// (<c>cppcache/include/geode/RegionService.hpp</c>); the
    /// <c>&lt;TKey, TValue&gt;</c> split is a C# addition (cppcache
    /// regions are untyped at the native layer, only typed in the
    /// C++/CLI <c>clicache</c> wrapper).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lookup-only</b>; never creates a region. Region instances
    /// are populated at <c>EnsureInitializedAsync</c> from
    /// <c>CacheXml.Regions</c> (Path A). Programmatic creation
    /// (Path B) lands in a later sub-phase.
    /// </para>
    /// <para>
    /// First successful call for a given path binds the
    /// <c>&lt;TKey, TValue&gt;</c> pair to that region for the
    /// lifetime of this cache. Subsequent calls with the same path
    /// must use the same type parameters or
    /// <see cref="InvalidOperationException"/> is thrown.
    /// </para>
    /// <para>
    /// Sub-region paths use <c>/</c> as separator
    /// (<c>"/parent/child"</c>); the leading slash is optional.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty or just <c>"/"</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The region exists but is already attached under different
    /// type parameters.
    /// </exception>
    IRegion<TKey, TValue>? GetRegion<TKey, TValue>(string path)
        where TKey : notnull;

    /// <summary>
    /// Untyped overload of <see cref="GetRegion{TKey, TValue}"/> —
    /// pure lookup. Returns <c>null</c> when no region with
    /// <paramref name="path"/> is registered. Mirrors cppcache
    /// <c>CacheImpl::getRegion</c>
    /// (<c>cppcache/src/CacheImpl.cpp:475</c>) directly: same path
    /// validation (empty / <c>"/"</c> rejected), same leading-slash
    /// strip, same first-segment + sub-region recursion.
    /// </summary>
    IRegion? GetRegion(string path);

    // Phase 1.4: IQueryService QueryService { get; }
    // Phase 1.x: IReadOnlyList<IRegion> RootRegions { get; }
    // Phase 2:   PdxInstanceFactory CreatePdxInstanceFactory(string className, ...);
}
