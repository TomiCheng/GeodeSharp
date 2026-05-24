using System.Diagnostics.CodeAnalysis;
using Geode.Client.Options;

namespace Geode.Client;

/// <summary>
/// Builds, retrieves, and disposes named <see cref="IGeodeCache"/> instances.
/// </summary>
public interface IGeodeCacheFactory : IAsyncDisposable
{

    /// <summary>
    /// Build, register, and initialise a new cache under <paramref name="cacheName"/>
    /// with built-in defaults (zero-config shortcut). The returned cache is fully
    /// initialised (TCCM bootstrapped) and ready to use.
    /// </summary>
    /// <param name="cacheName">Cache identifier; must be unique across the factory's lifetime.</param>
    /// <param name="ct">Cooperative cancellation for the build + initialise pipeline.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="cacheName"/> already exists.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    Task<IGeodeCache> CreateAsync(string cacheName, CancellationToken ct = default);

    /// <summary>
    /// Build, register, and initialise a new cache under <paramref name="cacheName"/>,
    /// running <paramref name="configure"/> against a fresh
    /// <see cref="GeodeClientOptions"/> beforehand.
    /// </summary>
    /// <param name="cacheName">Cache identifier; must be unique across the factory's lifetime.</param>
    /// <param name="configure">
    /// Optional callback that mutates a fresh <see cref="GeodeClientOptions"/>
    /// before the cache is built. The factory's <see cref="IServiceProvider"/>
    /// is forwarded so the callback can resolve <c>IConfiguration</c>,
    /// <c>IOptions&lt;T&gt;</c>, or other DI services when computing values.
    /// <see langword="null"/> leaves the cache on built-in defaults (equivalent
    /// to the 2-argument overload).
    /// </param>
    /// <param name="ct">Cooperative cancellation for the build + initialise pipeline.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="cacheName"/> already exists.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// Factory has been disposed.
    /// </exception>
    Task<IGeodeCache> CreateAsync(
        string cacheName,
        Action<GeodeClientOptions, IServiceProvider>? configure = null,
        CancellationToken ct = default);

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
