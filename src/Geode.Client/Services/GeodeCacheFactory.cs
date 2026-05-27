using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

internal sealed class GeodeCacheFactory(
    IServiceProvider rootServiceProvider,
    ILogger<GeodeCacheFactory> logger)
    : IGeodeCacheFactory, IAsyncDisposable
{

    private readonly ConcurrentDictionary<string, Lazy<AsyncServiceScope>> _caches = new(StringComparer.Ordinal);

    private int _disposed;

    /// <inheritdoc />
    public Task<IGeodeCache> CreateAsync(string cacheName, CancellationToken ct = default) =>
        CreateAsync(cacheName, configure: null, ct);

    /// <inheritdoc />
    public async Task<IGeodeCache> CreateAsync(
        string cacheName,
        Action<GeodeClientOptions, IServiceProvider>? configure = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var options = new GeodeClientOptions();
        configure?.Invoke(options, rootServiceProvider);

        var lazy = new Lazy<AsyncServiceScope>(
            () =>
            {
                var scope = rootServiceProvider.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                var sysProps = sp.GetRequiredService<SystemProperties>();
                SystemProperties.MergeSystemProperties(sysProps, cacheName, options);
                return scope;
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        if (!_caches.TryAdd(cacheName, lazy))
        {
            throw new InvalidOperationException($"Cache '{cacheName}' already exists.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_caches.TryRemove(cacheName, out var stored) && stored.IsValueCreated)
            {
                await stored.Value.DisposeAsync().ConfigureAwait(false);
            }
            throw new ObjectDisposedException(nameof(GeodeCacheFactory));
        }

        var cache = lazy.Value.ServiceProvider.GetRequiredService<GeodeCache>();
        await cache.InitializeAsync(ct).ConfigureAwait(false);
        return cache;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var snapshot = _caches.ToArray();
        _caches.Clear();

        foreach (var (_, lazy) in snapshot)
        {
            if (lazy.IsValueCreated)
            {
                await lazy.Value.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask<bool> DisposeCacheAsync(string cacheName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_caches.TryRemove(cacheName, out var lazy)) return false;

        if (lazy.IsValueCreated)
        {
            try
            {
                await lazy.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disposing scope for cache {CacheName}", cacheName);
            }
        }
        return true;
    }

    public IGeodeCache Get(string cacheName)
    {
        if (TryGet(cacheName, out var cache)) return cache;
        throw new KeyNotFoundException(
            $"No cache named '{cacheName}'. Call {nameof(CreateAsync)}(\"{cacheName}\") first.");
    }

    public bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache)
    {
        if (_caches.TryGetValue(cacheName, out var lazy) && lazy.IsValueCreated)
        {
            cache = lazy.Value.ServiceProvider.GetRequiredService<GeodeCache>();
            return true;
        }
        cache = null;
        return false;
    }

    public IReadOnlyCollection<string> CacheNames
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return [.. _caches.Keys];
        }
    }

}
