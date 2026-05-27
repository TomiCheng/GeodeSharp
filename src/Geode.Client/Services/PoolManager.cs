using System.Collections.Concurrent;
using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Services;

/// <summary>
/// Registry and lifecycle owner for named connection pools. Mirrors
/// cppcache <c>PoolManagerImpl</c>
/// (<c>cppcache/src/PoolManagerImpl.hpp/.cpp</c>); the public clicache
/// surface <c>PoolManager</c>
/// (<c>cppcache/include/geode/PoolManager.hpp</c>) is collapsed into
/// this single internal class &#x2014; .NET doesn't need the Pimpl
/// shim, and there is no MVP consumer use case that warrants exposing
/// the registry as a public interface.
/// </summary>
/// <remarks>
/// <para>
/// Threading: <c>ConcurrentDictionary</c> covers the registry; the
/// "first-added wins as default" rule is enforced via
/// <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>. cppcache's
/// <c>recursive_mutex</c> (<c>m_connectionPoolsLock</c>) is replaced by
/// these primitives.
/// </para>
/// <para>
/// Holds a back-pointer to the owning <see cref="GeodeCache"/> (exposed
/// via <see cref="Cache"/>) so scope-internal services routed through
/// the pool manager can reach per-cache state (options, system
/// properties, etc.) &#x2014; mirrors cppcache
/// <c>connManager-&gt;getCacheImpl()</c>'s role.
/// </para>
/// </remarks>
internal sealed class PoolManager(IServiceProvider serviceProvider)

    : IPoolManager, IAsyncDisposable
{

    private IPool? _defaultPool;
    private int _disposed;
    private readonly ConcurrentDictionary<string, ThinClientPoolDM> _pools = new(StringComparer.Ordinal);

    /// <summary>
    /// Register a pool under <paramref name="name"/>. The first
    /// successful registration also becomes <see cref="DefaultPool"/>.
    /// Mirrors cppcache <c>PoolManagerImpl::addPool</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a pool with the same name is already registered.
    /// </exception>
    internal void AddPool(string name, ThinClientPoolDM pool)
    {
        if (!_pools.TryAdd(name, pool))
        {
            throw new InvalidOperationException(
                $"Pool '{name}' is already registered.");
        }
        Interlocked.CompareExchange(ref _defaultPool, pool, null);
    }

    /// <summary>
    /// Deregister a pool. Mirrors cppcache
    /// <c>PoolManagerImpl::removePool</c>. Does not dispose the pool
    /// itself &#x2014; caller owns disposal lifecycle. Returns
    /// <c>true</c> when the name existed.
    /// </summary>
    internal bool RemovePool(string name)
    {
        return _pools.TryRemove(name, out _);
    }

    /// <summary>
    /// Close every registered pool. Mirrors cppcache
    /// <c>PoolManagerImpl::close(keepAlive)</c>; routes
    /// <paramref name="keepAlive"/> into each pool's
    /// <c>DestroyAsync</c>.
    /// </summary>
    /// <remarks>
    /// Idempotent. After this returns the manager rejects further
    /// <see cref="AddPool"/> calls (the disposed flag stays set).
    /// </remarks>
    public async Task CloseAsync(bool keepAlive = false, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Snapshot the values, then clear, before awaiting destroy —
        // late AddPool callers will see _disposed == 1 and throw.
        var pools = _pools.Values.ToArray();
        _pools.Clear();
        Volatile.Write(ref _defaultPool, null);

        // Aggregate failures the same way Task.WhenAll does; we don't
        // want one slow / faulty pool to mask the rest.
        await Task.WhenAll(pools.Select(p => p.DestroyAsync(keepAlive, ct)))
                  .ConfigureAwait(false);
    }

    /// <summary>
    /// New <see cref="PoolFactory"/> bound to this manager. Mirrors cppcache
    /// <c>PoolManager::createFactory()</c>.
    /// </summary>
    public PoolFactory CreateFactory()
    {
        return ActivatorUtilities.CreateInstance<PoolFactory>(serviceProvider);
    }

    /// <summary>Delegates to <see cref="CloseAsync"/> with <c>keepAlive: false</c>.</summary>
    public ValueTask DisposeAsync() => new(CloseAsync(keepAlive: false));

    /// <summary>
    /// Look up a pool by name. An empty <paramref name="name"/>
    /// returns <see cref="DefaultPool"/>, matching cppcache
    /// <c>PoolManagerImpl::find(name)</c>.
    /// </summary>
    public IPool? Find(string? name = null)
    {
        if (name is null) return DefaultPool;
        return _pools.TryGetValue(name, out var pool) ? pool : null;
    }

    /// <summary>
    /// Look up the pool a region was created on. Mirrors cppcache
    /// <c>PoolManagerImpl::find(region)</c> &#x2192;
    /// <c>find(region-&gt;getAttributes().getPoolName())</c>.
    /// </summary>
    public IPool? Find(IRegion region)
    {
        return Find(region.PoolName);
    }

    /// <summary>
    /// Snapshot of the registry. Mirrors cppcache
    /// <c>PoolManagerImpl::getAll()</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IPool> GetAll() =>
        _pools.ToDictionary(kv => kv.Key, kv => (IPool)kv.Value);

    /// <summary>
    /// First pool registered via <see cref="AddPool"/>. Mirrors
    /// cppcache <c>m_defaultPool</c>: the manager picks an arbitrary
    /// "default" so callers that look up by empty name still get
    /// something back.
    /// </summary>
    public IPool? DefaultPool => Volatile.Read(ref _defaultPool);

}

