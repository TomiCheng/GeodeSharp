/*
using System.Collections.Concurrent;
using Geode.Client.Internal;

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
/// The cppcache back-pointer <c>m_cache</c> is dropped: it existed only
/// for <c>createFactory()</c>, and that decision is deferred until we
/// pick how / whether to mirror <c>PoolFactory</c>.
/// </para>
/// </remarks>
internal sealed class PoolManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IPool> _pools =
        new(StringComparer.Ordinal);
    private IPool? _defaultPool;
    private int _disposed;

    /// <summary>
    /// First pool registered via <see cref="AddPool"/>. Mirrors
    /// cppcache <c>m_defaultPool</c>: the manager picks an arbitrary
    /// "default" so callers that look up by empty name still get
    /// something back.
    /// </summary>
    public IPool? DefaultPool => Volatile.Read(ref _defaultPool);

    /// <summary>
    /// Look up a pool by name. An empty <paramref name="name"/>
    /// returns <see cref="DefaultPool"/>, matching cppcache
    /// <c>PoolManagerImpl::find(name)</c>.
    /// </summary>
    public IPool? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0) return DefaultPool;
        return _pools.TryGetValue(name, out var pool) ? pool : null;
    }

    /// <summary>
    /// Look up the pool a region was created on. Mirrors cppcache
    /// <c>PoolManagerImpl::find(region)</c> &#x2192;
    /// <c>find(region-&gt;getAttributes().getPoolName())</c>.
    /// </summary>
    public IPool? Find(IRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Find(region.PoolName);
    }

    /// <summary>
    /// Snapshot of the registry. Mirrors cppcache
    /// <c>PoolManagerImpl::getAll()</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IPool> GetAll() => _pools;

    /// <summary>
    /// Register a pool under <paramref name="name"/>. The first
    /// successful registration also becomes <see cref="DefaultPool"/>.
    /// Mirrors cppcache <c>PoolManagerImpl::addPool</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a pool with the same name is already registered.
    /// </exception>
    internal void AddPool(string name, IPool pool)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(pool);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_pools.TryAdd(name, pool))
        {
            throw new InvalidOperationException(
                $"Pool '{name}' is already registered.");
        }

        // CompareExchange = "set only if still null". Loser of the
        // race keeps its slot; winner becomes the default forever.
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
        ArgumentNullException.ThrowIfNull(name);
        return _pools.TryRemove(name, out _);
    }

    /// <summary>
    /// Close every registered pool. Mirrors cppcache
    /// <c>PoolManagerImpl::close(keepAlive)</c>; routes
    /// <paramref name="keepAlive"/> into each
    /// <see cref="IPool.DestroyAsync(bool, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// After this returns the manager rejects further
    /// <see cref="AddPool"/> calls.
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

    public ValueTask DisposeAsync() => new(CloseAsync(keepAlive: false));

    // cppcache PoolManagerImpl::createFactory() is intentionally not
    // ported: pools are not constructed off the manager. Whether a
    // separate PoolFactory type is needed at all is undecided —
    // tracked in PORTING.md.
}

*/