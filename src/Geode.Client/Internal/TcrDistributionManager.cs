using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Non-pool, per-region distribution manager. Mirrors cppcache
/// <c>TcrDistributionManager</c>
/// (<c>cppcache/src/TcrDistributionManager.hpp:36</c>) — built by
/// <c>ThinClientRegion::initTCR()</c> when the region is NOT attached
/// to a <see cref="ThinClientPoolDM"/>, so each region runs its own
/// connection list against a shared <c>TcrConnectionManager</c>.
/// </summary>
/// <remarks>
/// <b>Phase 1.5 skeleton — never instantiated.</b> Per
/// pool-only-no-non-pool memory the non-pool code path is structurally
/// unreachable; this subclass exists only so the cppcache inheritance
/// shape <c>ThinClientBaseDM</c> &#x2192;
/// <c>ThinClientDistributionManager</c> &#x2192;
/// <c>TcrDistributionManager</c> is in place. <c>SendSyncRequestAsync</c>
/// / <c>SendRequestToEndpointAsync</c> throw <see cref="NotImplementedException"/>
/// — body lands only if the non-pool path is revived (Phase 1.5
/// pre-release audit decision, see memory).
/// cppcache ctor:
/// <c>(ThinClientRegion*, TcrConnectionManager&amp;)</c>.
/// </remarks>
internal sealed class TcrDistributionManager : ThinClientDistributionManager
{
    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "TcrDistributionManager (non-pool mode) is not implemented; see pool-only-no-non-pool memory.");

    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "TcrDistributionManager (non-pool mode) is not implemented; see pool-only-no-non-pool memory.");

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrEndpoint endpoint,
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "TcrDistributionManager (non-pool mode) is not implemented; see pool-only-no-non-pool memory.");

    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        TcrEndpoint endpoint,
        CancellationToken ct = default) =>
        throw new NotImplementedException(
            "TcrDistributionManager (non-pool mode) is not implemented; see pool-only-no-non-pool memory.");
}
