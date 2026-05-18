using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// HA-subscription pool variant. Mirrors cppcache <c>ThinClientPoolHADM</c>
/// (<c>cppcache/src/ThinClientPoolHADM.hpp/.cpp</c>) — chosen by the pool
/// factory when <see cref="CachePoolOptions.SubscriptionEnabled"/> is
/// <see langword="true"/>, so the pool owns a redundancy manager that
/// maintains the primary + secondary subscription channels.
/// </summary>
/// <remarks>
/// Phase 2+ entry point. Currently only the
/// <see cref="RemoveCallbackConnectionAsync"/> override is wired so the
/// per-endpoint cleanup chain runs end-to-end; the full surface
/// (<c>processMarker</c>, <c>getEndpointForNewCallBackConn</c>, the
/// <c>redundancyManager_</c> field and its lifecycle) lands with Phase 2+
/// HA / subscription work.
/// </remarks>
internal sealed class ThinClientPoolHADM(
    IServiceProvider serviceProvider,
    ILogger<ThinClientPoolDM> logger,
    CachePoolOptions xmlPool,
    GeodeClientOptions options,
    TcrConnectionManager connManager)
    : ThinClientPoolDM(serviceProvider, logger, xmlPool, options, connManager)
{
    /// <summary>
    /// Drop this HA pool's subscription-channel reference to
    /// <paramref name="endpoint"/>. Mirrors cppcache
    /// <c>ThinClientPoolHADM::removeCallbackConnection</c>
    /// (<c>ThinClientPoolHADM.cpp:287-289</c>) — delegates to the HA
    /// pool's <c>redundancyManager_</c>.
    /// </summary>
    protected override Task RemoveCallbackConnectionAsync(TcrEndpoint endpoint, CancellationToken ct)
    {
        _ = endpoint;
        _ = ct;
        // TODO Phase 2+ HA: delegate to ThinClientRedundancyManager.RemoveCallbackConnectionAsync(endpoint, ct)
        //   once the HA-pool's redundancy manager field + ThinClientRedundancyManager
        //   class land.
        return Task.CompletedTask;
    }
}
