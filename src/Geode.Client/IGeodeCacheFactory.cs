using System.Diagnostics.CodeAnalysis;

namespace Geode.Client;

/// <summary>
/// Builds, retrieves, and disposes named <see cref="IGeodeCache"/> instances.
/// </summary>
public interface IGeodeCacheFactory : IAsyncDisposable
{

    /// <summary>
    /// Build and register a new cache under <paramref name="cacheName"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="cacheName"/> already exists.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    IGeodeCache Create(string cacheName);

    /// <summary>
    /// Close and remove a single cache; <see langword="false"/> if no such cache.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    ValueTask<bool> DisposeCacheAsync(string cacheName);

    /// <summary>
    /// Get a built cache by name.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// No cache exists under <paramref name="cacheName"/>.
    /// </exception>
    IGeodeCache Get(string cacheName);

    /// <summary>
    /// Try to get a built cache by name; <see langword="false"/> if not found.
    /// </summary>
    bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache);

    /// <summary>
    /// Snapshot of names whose caches have been built.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    IReadOnlyCollection<string> CacheNames { get; }

}
