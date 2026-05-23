using System.Diagnostics.CodeAnalysis;

namespace Geode.Client;

/// <summary>
/// Builds, retrieves, and disposes named <see cref="IGeodeCache"/> instances.
/// </summary>
public interface IGeodeCacheFactory : IAsyncDisposable
{

    /// <summary>
    /// Build, register, and initialise a new cache under <paramref name="cacheName"/>.
    /// The returned cache is fully initialised (TCCM bootstrapped) and ready to use.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="cacheName"/> already exists.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    Task<IGeodeCache> CreateAsync(string cacheName, CancellationToken ct = default);

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
