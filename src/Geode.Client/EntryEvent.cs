namespace Geode.Client;

/// <summary>
/// Immutable snapshot of an entry-level cache event delivered to
/// <see cref="ICacheListener"/> callbacks. Mirrors cppcache
/// <c>EntryEvent</c> (<c>cppcache/include/geode/EntryEvent.hpp</c>).
/// </summary>
public sealed class EntryEvent(
    IRegion region,
    object key,
    object? oldValue,
    object? newValue,
    object? callbackArgument,
    bool remoteOrigin)
{
    /// <summary>The region the event originated in. cppcache <c>getRegion</c>.</summary>
    public IRegion Region { get; } = region;

    /// <summary>The affected key. cppcache <c>getKey</c>.</summary>
    public object Key { get; } = key;

    /// <summary>Prior value, or <see langword="null"/> on create / invalidate. cppcache <c>getOldValue</c>.</summary>
    public object? OldValue { get; } = oldValue;

    /// <summary>New value, or <see langword="null"/> on destroy / invalidate. cppcache <c>getNewValue</c>.</summary>
    public object? NewValue { get; } = newValue;

    /// <summary>Optional callback argument supplied with the op. cppcache <c>getCallbackArgument</c>.</summary>
    public object? CallbackArgument { get; } = callbackArgument;

    /// <summary>True when the event came from a remote (non-local) process. cppcache <c>remoteOrigin</c>.</summary>
    public bool RemoteOrigin { get; } = remoteOrigin;
}
