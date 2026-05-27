namespace Geode.Client.Internal;

/// <summary>
/// HA / subscription-enabled region subclass. Mirrors cppcache
/// <c>ThinClientHARegion</c>
/// (<c>cppcache/src/ThinClientHARegion.hpp:40</c>) — manages
/// interest-list functionality for native client regions backed by
/// Java HA queues. Inherits from <see cref="ThinClientRegion"/> and
/// overrides interest-list send / invalidate plus the HA-marker
/// bookkeeping.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 4+ skeleton:</b> empty body. Subscription / interest-list /
/// CQ are all currently cut from the IRegion surface
/// (<see cref="IRegion"/> xmldoc, "Cut per CLAUDE.md «Not implemented»").
/// This class exists so the cppcache inheritance shape
/// <c>ThinClientRegion</c> &#x2192; <c>ThinClientHARegion</c> is present
/// from the start; concrete migrations land when subscription support is
/// reinstated:
/// </para>
/// <list type="bullet">
/// <item><description><c>m_processedMarker</c> (volatile bool;
/// <c>ThinClientHARegion.hpp:69</c>) — set by the HA queue's first
/// "marker" event after failover so subsequent dispatches know the
/// queue is primed.</description></item>
/// <item><description><c>initTCR()</c> override
/// (<c>:53</c>) — HA-aware TCR init: locate the HA DM, register
/// interest, prime the marker.</description></item>
/// <item><description><c>getProcessedMarker</c> /
/// <c>setProcessedMarker(bool)</c> (<c>:55-59</c>) — marker bit
/// accessors.</description></item>
/// <item><description><c>addDisconnectedMessageToQueue()</c>
/// (<c>:60</c>) — enqueue a synthetic disconnect event so listeners
/// observe the gap.</description></item>
/// <item><description><c>getNoThrow_FullObject(eventId, fullObject,
/// versionTag)</c> protected override (<c>:63-65</c>) — pull the full
/// object from server when a delta apply needs the baseline (delta is
/// Phase deferred but the hook lives here).</description></item>
/// <item><description><c>handleMarker()</c> private override
/// (<c>:70</c>) — process the first marker after failover.</description></item>
/// <item><description><c>acquireGlobals(bool isFailover)</c> /
/// <c>releaseGlobals(bool isFailover)</c> (<c>:72-73</c>) — failover
/// locking around interest-list rebuild.</description></item>
/// <item><description><c>destroyDM(bool keepEndpoints)</c>
/// (<c>:75</c>) — HA DM teardown (drains subscription queue before
/// releasing the DM).</description></item>
/// </list>
/// <para>
/// cppcache ctor also takes <c>bool enableNotification = true</c>;
/// added when the notification-channel wiring lands.
/// </para>
/// </remarks>
internal sealed class ThinClientHARegion(
    IServiceProvider serviceProvider,
    string name,
    RegionInternal? parent,
    RegionAttributes attributes,
    bool enableNotification)
    : ThinClientRegion(serviceProvider, name, parent, attributes)
{
    // cppcache fields land here when subscription support is reinstated:
    //   volatile bool m_processedMarker  (ThinClientHARegion.hpp:69)
    //   RegionAttributes m_attributes    (:68 — shadow copy; cppcache
    //                                     pattern, may or may not survive
    //                                     the port given base already
    //                                     holds Attributes)
}
