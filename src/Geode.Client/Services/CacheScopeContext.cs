namespace Geode.Client.Services;

internal sealed class CacheScopeContext
{
    private bool _initialized;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Bind the cache name into this scope. Called exactly once by
    /// <see cref="GeodeCache"/> during its constructor.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public void Init(string name)
    {
        if (_initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(CacheScopeContext)} already initialized for cache '{Name}'.");
        }
        Name = name;
        _initialized = true;
    }
}
