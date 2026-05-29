namespace Geode.Client;

/// <summary>
/// Immutable snapshot of a region-level cache event delivered to
/// <see cref="ICacheListener"/> region callbacks.
/// </summary>
public sealed class RegionEvent(
    IRegion region,
    object? callbackArgument,
    bool remoteOrigin)
{
    /// <summary>The region the event originated in.</summary>
    public IRegion Region { get; } = region;

    /// <summary>Optional callback argument supplied with the op.</summary>
    public object? CallbackArgument { get; } = callbackArgument;

    /// <summary>True when the event came from a remote (non-local) process.</summary>
    public bool RemoteOrigin { get; } = remoteOrigin;
}
