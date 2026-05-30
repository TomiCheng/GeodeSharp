namespace Geode.Client.Internal;

/// <summary>
/// Inline helpers on <see cref="CacheEventFlags"/> mirroring cppcache
/// methods on the same bitfield wrapper
/// (<c>cppcache/src/RegionInternal.hpp</c>). C# enums can't carry
/// instance methods, so the cppcache <c>flags.invokeCacheWriter()</c>
/// style translates to extension methods here.
/// </summary>
internal static class CacheEventFlagsExtensions
{
    /// <summary>
    /// True when an op should invoke the <c>CacheWriter</c> hook.
    /// Mirrors cppcache <c>CacheEventFlags::invokeCacheWriter</c>
    /// (<c>cppcache/src/RegionInternal.hpp:115-119</c>): writer is
    /// suppressed when the op is a notification, eviction, expiration,
    /// or explicitly tagged <see cref="CacheEventFlags.NoCacheWriter"/>
    /// / <see cref="CacheEventFlags.NoCallbacks"/>.
    /// </summary>
    public static bool InvokeCacheWriter(this CacheEventFlags flags) =>
        (flags & (CacheEventFlags.Notification |
                  CacheEventFlags.Eviction |
                  CacheEventFlags.Expiration |
                  CacheEventFlags.NoCacheWriter |
                  CacheEventFlags.NoCallbacks)) == 0;

    /// <summary>
    /// True when the op is flagged local-only (no wire roundtrip).
    /// Mirrors cppcache <c>CacheEventFlags::isLocal</c>
    /// (<c>cppcache/src/RegionInternal.hpp:91</c>).
    /// </summary>
    public static bool IsLocal(this CacheEventFlags flags) =>
        (flags & CacheEventFlags.Local) != 0;

    /// <summary>
    /// True when the op is a server-pushed notification. Mirrors
    /// cppcache <c>CacheEventFlags::isNotification</c>
    /// (<c>cppcache/src/RegionInternal.hpp:93</c>).
    /// </summary>
    public static bool IsNotification(this CacheEventFlags flags) =>
        (flags & CacheEventFlags.Notification) != 0;

    /// <summary>
    /// True when both <c>CacheWriter</c> and <c>CacheListener</c>
    /// callbacks are explicitly suppressed for this op. Mirrors cppcache
    /// <c>CacheEventFlags::isNoCallbacks</c>
    /// (<c>cppcache/src/RegionInternal.hpp:109</c>).
    /// </summary>
    public static bool IsNoCallbacks(this CacheEventFlags flags) =>
        (flags & CacheEventFlags.NoCallbacks) != 0;

    /// <summary>
    /// True when the op is an eviction or expiration. Mirrors cppcache
    /// <c>CacheEventFlags::isEvictOrExpire</c>
    /// (<c>cppcache/src/RegionInternal.hpp:111-113</c>).
    /// </summary>
    public static bool IsEvictOrExpire(this CacheEventFlags flags) =>
        throw new NotImplementedException(
            "CacheEventFlagsExtensions.IsEvictOrExpire: pending LRU / expiry plumbing.");
}
