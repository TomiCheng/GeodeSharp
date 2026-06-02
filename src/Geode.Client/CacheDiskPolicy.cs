namespace Geode.Client;

/// <summary>
/// How LRU-evicted entries are handled.
/// </summary>
public enum CacheDiskPolicy
{
    /// <summary>
    /// Evicted entries are dropped from memory (no disk).
    /// </summary>
    None,

    /// <summary>
    /// Evicted entry values spill to disk, leaving a token in memory.
    /// </summary>
    Overflows,

    /// <summary>
    /// Full-region disk persistence (not implemented).
    /// </summary>
    Persist,
}
