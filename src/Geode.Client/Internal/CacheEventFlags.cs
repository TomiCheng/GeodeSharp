namespace Geode.Client.Internal;

/// <summary>
/// Event-source flags carried by internal <c>*NoThrow_remote</c> region
/// ops. Mirrors cppcache <c>CacheEventFlags</c>
/// (<c>cppcache/src/RegionInternal.hpp:46-120</c>) — a <c>uint16_t</c>
/// bitfield wrapper there, a <see cref="FlagsAttribute"/> enum here
/// (the static <c>NORMAL</c> / <c>LOCAL</c> / … instances + 9 ergonomic
/// operators in cppcache are already covered by .NET's flags-enum
/// machinery).
/// </summary>
/// <remarks>
/// Bit values match cppcache <c>GF_*</c> constants exactly. Phase 2+
/// (caching / listener / expiry / eviction) is where the call sites
/// actually pass values; today only <c>LocalRegion.isLocalOp</c>
/// accepts a parameter and every caller passes <see langword="null"/>.
/// </remarks>
[Flags]
internal enum CacheEventFlags : ushort
{
    /// <summary>cppcache <c>GF_NORMAL</c>: ordinary user-initiated op.</summary>
    Normal = 0x01,

    /// <summary>cppcache <c>GF_LOCAL</c>: client-side only, no wire roundtrip (<c>LocalDestroy</c> / <c>LocalPut</c> / <c>LocalInvalidate</c>).</summary>
    Local = 0x02,

    /// <summary>cppcache <c>GF_NOTIFICATION</c>: op originated from a server-pushed subscription event.</summary>
    Notification = 0x04,

    /// <summary>cppcache <c>GF_NOTIFICATION_UPDATE</c>: notification-driven update (delta-style propagation).</summary>
    NotificationUpdate = 0x08,

    /// <summary>cppcache <c>GF_EVICTION</c>: op triggered by LRU eviction.</summary>
    Eviction = 0x10,

    /// <summary>cppcache <c>GF_EXPIRATION</c>: op triggered by TTL / idle-timeout expiry.</summary>
    Expiration = 0x20,

    /// <summary>cppcache <c>GF_CACHE_CLOSE</c>: op driven by cache shutdown teardown.</summary>
    CacheClose = 0x40,

    /// <summary>cppcache <c>GF_NOCACHEWRITER</c>: skip <c>CacheWriter</c> callback for this op.</summary>
    NoCacheWriter = 0x80,

    /// <summary>cppcache <c>GF_NOCALLBACKS</c>: skip both <c>CacheWriter</c> and <c>CacheListener</c> callbacks.</summary>
    NoCallbacks = 0x100,
}
