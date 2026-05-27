namespace Geode.Client.Internal;

/// <summary>
/// Abstract intermediate DM. Mirrors cppcache
/// <c>ThinClientDistributionManager</c>
/// (<c>cppcache/src/ThinClientDistributionManager.hpp:31</c>) — the
/// shared parent of the non-pool concrete DM
/// (<see cref="TcrDistributionManager"/>) that owns its own connection
/// list / failover loop, separating it from the pool variant
/// (<see cref="ThinClientPoolDM"/>) which inherits straight off
/// <see cref="ThinClientBaseDM"/>.
/// </summary>
/// <remarks>
/// <b>Phase 1.5 skeleton:</b> empty body. Exists so the cppcache
/// inheritance shape <c>ThinClientBaseDM</c> &#x2192;
/// <c>ThinClientDistributionManager</c> &#x2192;
/// <c>TcrDistributionManager</c> is present from the start. Per
/// pool-only-no-non-pool memory we don't actually instantiate either
/// — the abstract member set (per-endpoint failover loop, single-endpoint
/// routing, etc.) lands when / if the non-pool path is revived.
/// cppcache ctor: <c>(TcrConnectionManager&amp;, ThinClientRegion*)</c>.
/// </remarks>
internal abstract class ThinClientDistributionManager : ThinClientBaseDM
{
    // No body — pure shape marker for the inheritance tree.
}
