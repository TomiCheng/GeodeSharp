using System.Collections.Concurrent;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCacheFactory"/>. Lazily constructs one
/// <see cref="GeodeCache"/> per registered name and caches it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>AddGeodeClient</c>. Construction
/// uses <see cref="IOptionsMonitor{TOptions}.Get(string)"/> so the
/// caller's named options bindings light up automatically.
/// </para>
/// <para>
/// <b>One DI scope per named cache.</b> Each <see cref="GeodeCache"/>
/// is built inside its own <see cref="AsyncServiceScope"/> so that
/// per-cache <c>Scoped</c> services (eventually: pool / connection /
/// metrics) don't alias across clusters. The scope's lifetime is
/// pinned to the cache: factory disposes the cache first, then the
/// scope, on shutdown. This mirrors the
/// <c>IHttpClientFactory</c> pattern for named clients.
/// </para>
/// <para>
/// <b>No hot reload.</b> We deliberately do not subscribe to
/// <c>IOptionsMonitor&lt;T&gt;.OnChange</c>. A built
/// <see cref="GeodeCache"/> owns an open TCP/TLS connection, handshake
/// state, membership id, and (eventually) a connection pool — those
/// cannot be swapped under live <c>IRegion&lt;K, V&gt;</c> references
/// without breaking in-flight ops. <see cref="IOptionsMonitor{T}"/> is
/// chosen only for its <c>Get(name)</c> + singleton-lifetime support;
/// the change-notification half is intentionally unused.
/// </para>
/// </remarks>
internal sealed class GeodeCacheFactory(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GeodeClientOptions> optionsMonitor,
    ILogger<GeodeCacheFactory> logger)
    : IGeodeCacheFactory, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<ScopedCacheEntry>> _caches =
        new(StringComparer.Ordinal);
    private int _disposed;

    public IGeodeCache Get() => Get(MsOptions.DefaultName);

    public IGeodeCache Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Lazy<T> guarantees the build-cache delegate runs exactly once
        // even if two threads race past GetOrAdd. Without it the loser
        // would create an AsyncServiceScope that nobody disposes.
        var entry = _caches.GetOrAdd(name, n => new Lazy<ScopedCacheEntry>(
            () => Build(n),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return entry.Value.Cache;
    }

    /// <summary>
    /// Build a <see cref="GeodeCache"/> inside its own
    /// <see cref="AsyncServiceScope"/>. Sync, no wire I/O — the cache
    /// itself initialises lazily on the first wire-touching op.
    /// </summary>
    private ScopedCacheEntry Build(string name)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var options = optionsMonitor.Get(name);
            // ActivatorUtilities needs a concrete type; T = IGeodeCache
            // would throw "Instances of abstract classes cannot be
            // created." Implicit upcast back to IGeodeCache on return.
            var cache = (IGeodeCache)ActivatorUtilities.CreateInstance<GeodeCache>(
                scope.ServiceProvider, name, options);
            return new ScopedCacheEntry(cache, scope);
        }
        catch
        {
            // Avoid leaking the scope if cache construction fails.
            // DisposeAsync would normally do this for stored entries,
            // but a thrown ctor never reaches the dictionary.
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Cascade <see cref="IAsyncDisposable.DisposeAsync"/> to every
    /// cached <see cref="IGeodeCache"/> and then to the per-cache
    /// <see cref="AsyncServiceScope"/>. After this returns,
    /// <see cref="Get(string)"/> throws
    /// <see cref="ObjectDisposedException"/>. Idempotent.
    /// </summary>
    /// <remarks>
    /// Per-cache and per-scope disposal exceptions are logged via
    /// <c>ILogger&lt;GeodeCacheFactory&gt;</c> and swallowed — one bad
    /// cache must not block the others' close path, and rethrowing
    /// from a finalizer-shaped path would mask the original exception
    /// that triggered <c>await using</c> shutdown.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // CAS so concurrent DisposeAsync calls only run the body once.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var snapshot = _caches.ToArray();
        _caches.Clear();

        foreach (var (name, lazy) in snapshot)
        {
            // Skip Lazy entries that lost the GetOrAdd race and never
            // had .Value invoked — there's no scope or cache to dispose.
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            var (cache, scope) = lazy.Value;

            try
            {
                await cache.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disposing cache {CacheName}", name);
            }

            try
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disposing scope for cache {CacheName}", name);
            }
        }
    }

    /// <summary>
    /// Pair of a built <see cref="IGeodeCache"/> and the
    /// <see cref="AsyncServiceScope"/> that owns its scoped services.
    /// </summary>
    private readonly record struct ScopedCacheEntry(IGeodeCache Cache, AsyncServiceScope Scope);
}
