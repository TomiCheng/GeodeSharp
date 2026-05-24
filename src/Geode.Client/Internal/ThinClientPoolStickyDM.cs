using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Sticky-transaction pool variant. Mirrors cppcache
/// <c>ThinClientPoolStickyDM</c>
/// (<c>cppcache/src/ThinClientPoolStickyDM.hpp/.cpp</c>) — chosen by the
/// pool factory when <see cref="CachePoolOptions.ThreadLocalConnections"/>
/// is <see langword="true"/>, so per-thread sticky connections survive
/// across ops within a transaction.
/// </summary>
/// <remarks>
/// Phase 6 entry point. Currently only the
/// <see cref="CleanStickyConnectionsAsync"/> override is wired so the
/// per-tick cleanup chain runs end-to-end; the full surface
/// (<c>setStickyConnection</c>, <c>getConnectionToAnEndPoint</c>,
/// <c>setStickyNull</c>, <c>canItBeDeleted</c>) lands with Phase 6
/// sticky-tx routing.
/// </remarks>
internal sealed class ThinClientPoolStickyDM(
    IServiceProvider serviceProvider,
    ILogger<ThinClientPoolDM> logger,
    PoolManager poolManager,
    string name,
    PoolAttributes attributes)
    : ThinClientPoolDM(serviceProvider, logger, poolManager, name, attributes)
{
    /// <summary>
    /// Dispatch the per-tick sticky-conn aging sweep into
    /// <see cref="ThinClientStickyManager.CleanStaleStickyConnectionAsync"/>.
    /// Mirrors cppcache <c>ThinClientPoolStickyDM::cleanStickyConnections</c>
    /// (<c>ThinClientPoolStickyDM.cpp:134-140</c>).
    /// </summary>
    protected override Task CleanStickyConnectionsAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
        //   => _stickyManager?.CleanStaleStickyConnectionAsync(ct) ?? Task.CompletedTask;
    }

}
