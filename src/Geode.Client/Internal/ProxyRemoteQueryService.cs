/*
namespace Geode.Client.Internal;

/// <summary>
/// Per-user proxy variant of <see cref="IQueryService"/>. Mirrors
/// cppcache <c>ProxyRemoteQueryService</c>
/// (<c>cppcache/src/ProxyRemoteQueryService.hpp/.cpp</c>); Phase 3
/// (multi-user security) placeholder so the wiring point exists when
/// <c>AuthenticatedView</c> lands.
/// </summary>
/// <remarks>
/// cppcache's ctor takes <c>AuthenticatedView*</c> and stores the
/// real per-pool <see cref="RemoteQueryService"/>; <c>newQuery</c>
/// dispatches via the AuthenticatedView's selected pool to that
/// pool's actual query service. Continuous Query methods on cppcache's
/// version are out of scope until Phase 2.
/// </remarks>
internal sealed class ProxyRemoteQueryService : IQueryService
{
    /// <inheritdoc cref="IQueryService.NewQuery{T}(string)" />
    public IQuery<T> NewQuery<T>(string oql)
    {
        // Mirrors cppcache ProxyRemoteQueryService::newQuery
        // (cppcache/src/ProxyRemoteQueryService.cpp:33-50): resolve the
        // AuthenticatedView's pool DM, then forward to that pool's real
        // RemoteQueryService. Phase 3 scope — nothing constructs this
        // class today.
        throw new NotImplementedException(
            "Phase 3 multi-user authentication is not yet implemented.");
    }
}

*/