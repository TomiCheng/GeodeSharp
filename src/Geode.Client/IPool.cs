
namespace Geode.Client;

/// <summary>
/// A named connection pool to a Geode cluster. Mirrors cppcache
/// <c>Pool</c> (<c>cppcache/include/geode/Pool.hpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal.</b> No MVP consumer use case exposes this surface;
/// <c>IGeodeCache</c> + <c>IRegion</c> covers everything callers need.
/// Lift to <c>public</c> when monitoring / advanced lifecycle hooks
/// require it (internal &#x2192; public is non-breaking; the reverse
/// is not).
/// </para>
/// <para>
/// Sole implementor is the cppcache equivalent <c>ThinClientPoolDM</c>
/// (multi-inherits <c>ThinClientBaseDM</c> + <c>Pool</c> +
/// <c>ConnectionQueue</c>); subclasses <c>ThinClientPoolHADM</c> /
/// <c>ThinClientPoolStickyDM</c> add HA / sticky-tx behaviour.
/// </para>
/// </remarks>
public interface IPool : IAsyncDisposable
{
    ///// <summary>
    ///// Tear the pool down. Mirrors cppcache <c>Pool::destroy(keepAlive)</c>.
    ///// </summary>
    ///// <param name="keepAlive">
    ///// When <c>true</c>, leaves subscription queues alive on the server
    ///// for durable clients (cppcache semantics). Until durable
    ///// subscriptions ship (Phase 2+) implementations may treat this as
    ///// a no-op equivalent to <c>false</c>.
    ///// </param>
    ///// <param name="ct">Cooperative cancellation.</param>
    ///// <remarks>
    ///// <c>DisposeAsync</c> on <see cref="IAsyncDisposable"/> is
    ///// expected to delegate to <c>DestroyAsync(keepAlive: false)</c>
    ///// so <c>using</c> blocks Just Work.
    ///// </remarks>
    //Task DestroyAsync(bool keepAlive = false, CancellationToken ct = default);

    /// <summary>
    /// Pool-scoped OQL query factory.
    /// </summary>
    IQueryService QueryService { get; }

    //// TODO Phase 1.5:
    ////   string Name { get; }
    ////   bool IsDestroyed { get; }
    ////   PoolOptions Options { get; }                       // replaces 30+ cppcache getters
    ////   IReadOnlyList<string> Locators { get; }
    ////   IReadOnlyList<string> Servers { get; }
    ////
    //// Skipped (cppcache surface we will not expose):
    ////   releaseThreadLocalConnection() — bucket 1, AsyncLocal<T>
    ////   createAuthenticatedView()       — Phase 3
    ////   getPendingEventCount()          — bucket 1, Meter counter
}

