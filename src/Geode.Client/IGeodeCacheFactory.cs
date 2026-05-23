/*
using System.Diagnostics.CodeAnalysis;
using Geode.Client.Options;

namespace Geode.Client;

/// <summary>
/// Builds, retrieves, and disposes named <see cref="IGeodeCache"/> instances.
/// </summary>
public interface IGeodeCacheFactory
{
    /// <summary>Get a built cache by name.</summary>
    /// <exception cref="KeyNotFoundException">No cache exists under <paramref name="cacheName"/>.</exception>
    /// <exception cref="ObjectDisposedException">Factory has been disposed.</exception>
    IGeodeCache Get(string cacheName = "");

    /// <summary>Try to get a built cache by name. Does not build.</summary>
    /// <returns><c>true</c> if found.</returns>
    /// <exception cref="ObjectDisposedException">Factory has been disposed.</exception>
    bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache);

    /// <summary>
    /// Build a new cache. <paramref name="configName"/> selects the
    /// registered options; <paramref name="action"/> optionally tweaks
    /// a clone of those options before construction (original config is
    /// not mutated).
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="cacheName"/> already exists.</exception>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">Resolved options failed validation.</exception>
    /// <exception cref="ObjectDisposedException">Factory has been disposed.</exception>
    IGeodeCache Create(
        string cacheName = "",
        string configName = "",
        Action<IServiceProvider, GeodeClientOptions>? action = null);

    /// <summary>Snapshot of names whose caches have been built.</summary>
    /// <exception cref="ObjectDisposedException">Factory has been disposed.</exception>
    IReadOnlyCollection<string> CacheNames { get; }

    /// <summary>Close and remove a cache.</summary>
    /// <returns><c>true</c> if removed, <c>false</c> if no cache existed under that name.</returns>
    /// <exception cref="ObjectDisposedException">Factory has been disposed.</exception>
    ValueTask<bool> RemoveAsync(string cacheName);
}

*/