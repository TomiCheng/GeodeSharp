namespace Geode.Client;

/// <summary>
/// A connection to a single Geode cluster. Obtained from
/// <see cref="IGeodeCacheFactory"/> (or, for the unnamed registration,
/// resolved directly from DI).
/// </summary>
/// <remarks>
/// Mirrors the cppcache <c>Cache</c> / <c>GeodeCache</c> /
/// <c>RegionService</c> chain, collapsed into a single .NET-shaped
/// interface — the cppcache split exists to support multi-user
/// authenticated views, which MVP does not.
/// </remarks>
public interface IGeodeCache : IAsyncDisposable
{
    /// <summary>
    /// Logical name this cache was registered under. Empty string for
    /// the unnamed default.
    /// </summary>
    string Name { get; }

    /// <summary>Whether <see cref="CloseAsync"/> has been called.</summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gracefully close the underlying connection(s). Subsequent calls
    /// are a no-op.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
