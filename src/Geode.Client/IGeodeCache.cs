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
    /// cancellation for everyone awaiting that init — pass a token
    /// you control if you care.
    /// </para>
    /// </remarks>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    /// <summary>
    /// Gracefully close the underlying connection(s). Subsequent calls
    /// are a no-op.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
