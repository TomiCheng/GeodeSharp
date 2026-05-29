namespace Geode.Client;

/// <summary>
/// User-implemented hook invoked on a cache miss to load the value for a
/// key from an external source.
/// </summary>
public interface ICacheLoader
{
    /// <summary>Produce the value for <paramref name="key"/> on a miss, or <see langword="null"/> if none.</summary>
    object? Load(IRegion region, object key, object? callbackArgument);

    /// <summary>When the loader is detached or the cache is closed.</summary>
    void Close(IRegion region) { }
}
