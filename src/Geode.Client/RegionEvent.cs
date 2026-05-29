namespace Geode.Client;

/// <summary>
/// Immutable snapshot of a region-level cache event delivered to
/// <see cref="ICacheListener"/> region callbacks. Mirrors cppcache
/// <c>RegionEvent</c> (<c>cppcache/include/geode/RegionEvent.hpp</c>).
/// </summary>
public sealed class RegionEvent(
    IRegion region,
    object? callbackArgument,
    bool remoteOrigin)
{
    /// <summary>The region the event originated in. cppcache <c>getRegion</c>.</summary>
    public IRegion Region { get; } = region;

    /// <summary>Optional callback argument supplied with the op. cppcache <c>getCallbackArgument</c>.</summary>
    public object? CallbackArgument { get; } = callbackArgument;

    /// <summary>True when the event came from a remote (non-local) process. cppcache <c>remoteOrigin</c>.</summary>
    public bool RemoteOrigin { get; } = remoteOrigin;
}
