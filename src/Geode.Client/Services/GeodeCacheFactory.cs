using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Geode.Client.Internal;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCacheFactory"/>. Owns one
/// <see cref="AsyncServiceScope"/> per built cache; disposal cascades
/// from the factory's <see cref="DisposeAsync"/> or
/// <see cref="RemoveAsync"/> into the scope's scoped services
/// (Cache / PoolManager / SerializationRegistry / …).
/// </summary>
/// <remarks>
/// <para>
/// Construction is explicit — see <see cref="Create"/>. <see cref="Get"/>
/// throws <see cref="KeyNotFoundException"/> on miss; no lazy
/// auto-build. Production / test behaviour stay symmetric and
/// "forgot to register" or "forgot to Create" surface at the same
/// failure point.
/// </para>
/// <para>
/// Options snapshots are captured at <see cref="Create"/> time;
/// runtime mutation of <c>appsettings.json</c> / <c>IOptionsMonitor</c>
/// does not propagate to already-built caches. Rebuild via
/// <see cref="RemoveAsync"/> + <see cref="Create"/>.
/// </para>
/// </remarks>
internal sealed class GeodeCacheFactory(
    IServiceProvider rootServiceProvider,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GeodeClientOptions> optionsMonitor,
    ILogger<GeodeCacheFactory> logger)
    : IGeodeCacheFactory, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ScopedCacheEntry> _caches =
        new(StringComparer.Ordinal);
    private int _disposed;

    public IGeodeCache Get(string cacheName = "")
    {
        if (TryGet(cacheName, out var cache)) return cache;
        throw new KeyNotFoundException(
            $"No cache named '{cacheName}'. Call {nameof(Create)}(\"{cacheName}\", ...) first.");
    }

    public bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache)
    {
        ArgumentNullException.ThrowIfNull(cacheName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_caches.TryGetValue(cacheName, out var entry))
        {
            cache = entry.Cache;
            return true;
        }
        cache = null;
        return false;
    }

    public IGeodeCache Create(
        string cacheName = "",
        string configName = "",
        Action<IServiceProvider, GeodeClientOptions>? action = null)
    {
        ArgumentNullException.ThrowIfNull(cacheName);
        ArgumentNullException.ThrowIfNull(configName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Early reject — saves a wasted scope build when the caller
        // already-bound name. TryAdd below is still the authoritative
        // race-safe check.
        if (_caches.ContainsKey(cacheName))
        {
            throw new InvalidOperationException(
                $"Cache '{cacheName}' already exists. " +
                $"Call {nameof(RemoveAsync)} first to rebuild.");
        }

        var baseOptions = optionsMonitor.Get(configName);

        var scope = scopeFactory.CreateAsyncScope();
        IGeodeCache cache;
        try
        {
            var options = baseOptions;
            if (action is not null)
            {
                // DeepClone so action mutations stay local to this
                // cache — IOptionsMonitor's cached options instance is
                // not touched, so a second Create against the same
                // configName starts from a fresh copy of the original.
                var clone = baseOptions.DeepClone();
                action(rootServiceProvider, clone);

                // Validate the modified clone. configName is the
                // diagnostic label (matches the validator wrapper's
                // prefix shape for IOptions consumers).
                var prefix = string.IsNullOrEmpty(configName)
                    ? nameof(GeodeClientOptions)
                    : $"{nameof(GeodeClientOptions)}[{configName}]";
                var failures = clone.Validate(prefix).ToList();
                if (failures.Count > 0)
                {
                    throw new OptionsValidationException(
                        nameof(GeodeClientOptions),
                        typeof(GeodeClientOptions),
                        failures);
                }
                options = clone;
            }

            // Bind cacheName + final options into the scope so every
            // scope-internal service (Cache, PoolManager,
            // SerializationRegistry, …) resolves against this snapshot.
            scope.ServiceProvider
                .GetRequiredService<CacheScopeContext>()
                .Initialize(cacheName, options);

            cache = (IGeodeCache)scope.ServiceProvider.GetRequiredService<Cache>();
        }
        catch
        {
            // Sync-over-async dispose — Create is synchronous and the
            // partly-built scope has not started wire I/O.
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        var entry = new ScopedCacheEntry(cache, scope);
        if (!_caches.TryAdd(cacheName, entry))
        {
            // Lost the race against another concurrent Create with the
            // same cacheName. Drop our build, surface the conflict.
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException(
                $"Cache '{cacheName}' already exists. " +
                $"Call {nameof(RemoveAsync)} first to rebuild.");
        }

        // Disposed-during-build race: DisposeAsync may have fired
        // between our entry disposed-check and our TryAdd, snapshotting
        // _caches BEFORE our entry landed. Re-check; if disposed,
        // tear down our scope ourselves (DisposeAsync's snapshot loop
        // won't see it).
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_caches.TryRemove(cacheName, out var stored))
            {
                stored.Scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            throw new ObjectDisposedException(nameof(GeodeCacheFactory));
        }

        return cache;
    }

    public IReadOnlyCollection<string> CacheNames
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _caches.Keys.ToArray();
        }
    }

    public async ValueTask<bool> RemoveAsync(string cacheName)
    {
        ArgumentNullException.ThrowIfNull(cacheName);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_caches.TryRemove(cacheName, out var entry)) return false;
        await DisposeEntryAsync(cacheName, entry).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Dispose every per-cache <see cref="AsyncServiceScope"/>; the
    /// scope's own dispose cascades into <see cref="Cache"/> and the
    /// other scoped services in reverse-resolve order. After this
    /// returns, the factory rejects all operations with
    /// <see cref="ObjectDisposedException"/>. Idempotent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var snapshot = _caches.ToArray();
        _caches.Clear();

        foreach (var (name, entry) in snapshot)
        {
            await DisposeEntryAsync(name, entry).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dispose a single cache entry's scope, logging any error so one
    /// bad scope doesn't block the rest of the pipeline. Shared by
    /// <see cref="DisposeAsync"/> and <see cref="RemoveAsync"/>.
    /// </summary>
    private async ValueTask DisposeEntryAsync(string cacheName, ScopedCacheEntry entry)
    {
        try
        {
            await entry.Scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error disposing scope for cache {CacheName}", cacheName);
        }
    }

    /// <summary>
    /// Pair of a built <see cref="IGeodeCache"/> and the
    /// <see cref="AsyncServiceScope"/> that owns its scoped services.
    /// </summary>
    private readonly record struct ScopedCacheEntry(IGeodeCache Cache, AsyncServiceScope Scope);
}
