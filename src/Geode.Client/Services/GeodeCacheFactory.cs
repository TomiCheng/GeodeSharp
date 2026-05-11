using System.Collections.Concurrent;
using Geode.Client.Internal;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="IGeodeCacheFactory"/>. Lazily constructs one
/// <see cref="Cache"/> per registered name and caches it.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton by <c>AddGeodeClient</c>. Construction
/// uses <see cref="IOptionsMonitor{TOptions}.Get(string)"/> so the
/// caller's named options bindings light up automatically.
/// </para>
/// <para>
/// <b>One DI scope per named cache.</b> Each <see cref="Cache"/>
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
/// <see cref="Cache"/> owns an open TCP/TLS connection, handshake
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
    /// Build a <see cref="Cache"/> inside its own
    /// <see cref="AsyncServiceScope"/>. Sync, no wire I/O — the cache
    /// itself initialises lazily on the first wire-touching op.
    /// </summary>
    private ScopedCacheEntry Build(string name)
    {
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var options = optionsMonitor.Get(name);

            // Bind name + options into the scope so every scope-internal
            // service (ClientProxyMembershipIdBuilder, TcrConnection,
            // ThinClientPoolDM, ...) sees the right cache's options
            // without anyone reaching back into IOptionsMonitor with a
            // hard-coded name. This is what lets named registrations
            // (AddGeodeClient(opts, "g1")) compose with the rest of the
            // pipeline — IOptions<T> alone always returns the unnamed
            // default and would alias clusters together.
            scope.ServiceProvider
                .GetRequiredService<CacheScopeContext>()
                .Initialize(name, options);

            // Cache is registered as Scoped (see AddCore), so the scope
            // owns its lifetime. name + options flow in via the
            // CacheScopeContext initialised above. Implicit upcast back
            // to IGeodeCache on return.
            var cache = (IGeodeCache)scope.ServiceProvider.GetRequiredService<Cache>();
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
    /// Dispose every per-cache <see cref="AsyncServiceScope"/>; the
    /// scope's own dispose cascades into <see cref="Cache"/> and the
    /// other scoped services (<see cref="PoolManager"/>, ...) in
    /// reverse-resolve order. After this returns,
    /// <see cref="Get(string)"/> throws
    /// <see cref="ObjectDisposedException"/>. Idempotent.
    /// </summary>
    /// <remarks>
    /// Per-scope disposal exceptions are logged via
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
            // had .Value invoked — there's no scope to dispose.
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            var (_, scope) = lazy.Value;

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
