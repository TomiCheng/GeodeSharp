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

    // Phase 1.2: IRegion<TKey, TValue> GetRegion<TKey, TValue>(string name);
    // Phase 1.4: IQueryService QueryService { get; }
    // Phase 1.x: IReadOnlyList<IRegion> RootRegions { get; }
    // Phase 2:   PdxInstanceFactory CreatePdxInstanceFactory(string className, ...);
}
