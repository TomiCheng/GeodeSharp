using System.Net;
using Geode.Client.Services;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-mode endpoint subclass. Mirrors cppcache
/// <c>TcrPoolEndPoint</c>
/// (<c>cppcache/src/TcrPoolEndPoint.hpp/.cpp</c>) — the variant of
/// <see cref="TcrEndpoint"/> that owns a single
/// <see cref="ThinClientPoolDM"/> back-reference and routes pool-mode
/// concerns (per-pool DM ref via <c>getPoolHADM</c>, pool-mode
/// <c>registerDM</c>, pool-aware <c>handleIOException</c>,
/// <c>handleNotificationStats</c>) through that ref.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.5 skeleton:</b> empty body. Exists so the cppcache
/// inheritance shape <c>TcrEndpoint</c> &#x2192; <c>TcrPoolEndPoint</c>
/// is present from the start; concrete migrations land step by step:
/// </para>
/// <list type="bullet">
/// <item><description><c>m_dm</c> field + <c>getPoolHADM()</c>
/// accessor — pull the pool ref out of the base class's
/// <c>_distMgrs</c> list (which currently doubles as the multi-pool
/// broadcast list because the base has no single-DM concept)
/// once pool callers switch to constructing this subclass.</description></item>
/// <item><description>Pool-aware <c>RegisterDMAsync</c> override —
/// cppcache <c>TcrPoolEndPoint::registerDM</c> diverges from the
/// non-pool base around subscription / redundancy.</description></item>
/// <item><description>Per-endpoint <c>handleNotificationStats</c> —
/// fires <see cref="PoolStatistics"/> <c>ReceivedBytes</c> +
/// <c>MessagesBeingReceived</c> (#21, Phase 2+ subscription
/// channel).</description></item>
/// </list>
/// <para>
/// Until callers (<see cref="TcrConnectionManager.AddRefToTcrEndpointAsync"/>
/// in pool mode) switch to constructing <see cref="TcrPoolEndPoint"/>,
/// pool-mode endpoints stay base <see cref="TcrEndpoint"/> instances
/// and this subclass is unused. The unsealed base + empty subclass
/// is the structural placeholder for the migration.
/// </para>
/// </remarks>
internal sealed class TcrPoolEndPoint(
    IServiceProvider serviceProvider,
    ILogger<TcrEndpoint> logger,
    CacheScopeContext cacheScopeContext,
    DnsEndPoint endpoint)
    : TcrEndpoint(serviceProvider, logger, cacheScopeContext, endpoint)
{
    // cppcache m_dm: ThinClientPoolDM* — set in ctor; routed through
    // every pool-mode override. Migration pending; see class xmldoc.
}
