namespace Geode.Client;

/// <summary>
/// Predefined region attribute presets passed to
/// <see cref="IGeodeCache.CreateRegionFactory(RegionShortcut)"/>.
/// </summary>
public enum RegionShortcut
{
    /// <summary>
    /// No local state; every operation forwards to a server.
    /// </summary>
    Proxy,

    /// <summary>
    /// Local state plus server fallback: misses go to the server and the
    /// returned value is cached locally.
    /// </summary>
    CachingProxy,

    /// <summary>
    /// <see cref="CachingProxy"/> with an LRU bound on local entries
    /// (default limit: 100000).
    /// </summary>
    CachingProxyEntryLru,

    /// <summary>
    /// Local-only; never reaches a server.
    /// </summary>
    Local,

    /// <summary>
    /// <see cref="Local"/> with an LRU bound on local entries
    /// (default limit: 100000).
    /// </summary>
    LocalEntryLru,
}
