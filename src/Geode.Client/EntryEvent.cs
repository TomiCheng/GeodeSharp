namespace Geode.Client;

/// <summary>
/// Immutable snapshot of an entry-level cache event delivered to
/// <see cref="ICacheListener"/> callbacks.
/// </summary>
public sealed class EntryEvent(
    IRegion region,
    object key,
    object? oldValue,
    object? newValue,
    object? callbackArgument,
    bool remoteOrigin)
{
    /// <summary>The region the event originated in.</summary>
    public IRegion Region { get; } = region;

    /// <summary>The affected key.</summary>
    public object Key { get; } = key;

    /// <summary>Prior value, or <see langword="null"/> on create / invalidate.</summary>
    public object? OldValue { get; } = oldValue;

    /// <summary>New value, or <see langword="null"/> on destroy / invalidate.</summary>
    public object? NewValue { get; } = newValue;

    /// <summary>Optional callback argument supplied with the op.</summary>
    public object? CallbackArgument { get; } = callbackArgument;

    /// <summary>True when the event came from a remote (non-local) process.</summary>
    public bool RemoteOrigin { get; } = remoteOrigin;
}
