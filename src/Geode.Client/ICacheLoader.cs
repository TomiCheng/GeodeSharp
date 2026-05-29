namespace Geode.Client;

/// <summary>
/// User-implemented hook invoked on a cache miss to load the value for a
/// key from an external source.
/// </summary>
public interface ICacheLoader
{
    /// <summary>Produce the value for <paramref name="key"/> on a miss, or <see langword="null"/> if none.</summary>
    ValueTask<object?> LoadAsync(IRegion region, object key, object? callbackArgument, CancellationToken ct = default);

    /// <summary>When the loader is detached or the cache is closed.</summary>
    ValueTask CloseAsync(IRegion region, CancellationToken ct = default) => default;
}
