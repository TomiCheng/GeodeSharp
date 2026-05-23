using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

internal sealed class GeodeCacheFactory(
    IServiceProvider rootServiceProvider,
    ILogger<GeodeCacheFactory> logger)
    : IGeodeCacheFactory, IAsyncDisposable
{

    private readonly ConcurrentDictionary<string, Lazy<GeodeCache>> _caches = new(StringComparer.Ordinal);

    private int _disposed;

    public IGeodeCache Create(string cacheName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var lazy = new Lazy<GeodeCache>(
            () => ActivatorUtilities.CreateInstance<GeodeCache>(rootServiceProvider, cacheName),
            LazyThreadSafetyMode.ExecutionAndPublication);

        if (!_caches.TryAdd(cacheName, lazy))
        {
            throw new InvalidOperationException($"Cache '{cacheName}' already exists.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            if (_caches.TryRemove(cacheName, out var stored)
                && stored.IsValueCreated
                && (object)stored.Value is IAsyncDisposable d)
            {
                d.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            throw new ObjectDisposedException(nameof(GeodeCacheFactory));
        }

        return lazy.Value;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        var snapshot = _caches.ToArray();
        _caches.Clear();

        foreach (var (_, lazy) in snapshot)
        {
            if (lazy.IsValueCreated && (object)lazy.Value is IAsyncDisposable d)
            {
                await d.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async ValueTask<bool> DisposeCacheAsync(string cacheName)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_caches.TryRemove(cacheName, out var lazy)) return false;

        if (lazy.IsValueCreated && (object)lazy.Value is IAsyncDisposable d)
        {
            try
            {
                await d.DisposeAsync().ConfigureAwait(false);
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
            $"No cache named '{cacheName}'. Call {nameof(Create)}(\"{cacheName}\") first.");
    }

    public bool TryGet(string cacheName, [NotNullWhen(true)] out IGeodeCache? cache)
    {
        if (_caches.TryGetValue(cacheName, out var lazy))
        {
            cache = lazy.Value;
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
