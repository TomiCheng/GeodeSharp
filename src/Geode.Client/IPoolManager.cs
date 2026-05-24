namespace Geode.Client;

/// <summary>
/// Registry and lifecycle owner for named connection pools.
/// </summary>
public interface IPoolManager : IAsyncDisposable
{
    /// <summary>The first pool registered with this manager, or <see langword="null"/> if none.</summary>
    IPool? DefaultPool { get; }

    /// <summary>New <see cref="PoolFactory"/> bound to this manager.</summary>
    PoolFactory CreateFactory();

    /// <summary>Close every registered pool; idempotent.</summary>
    Task CloseAsync(bool keepAlive = false, CancellationToken ct = default);

    /// <summary>Look up a pool by name; <see langword="null"/> when missing. <see langword="null"/> name returns <see cref="DefaultPool"/>.</summary>
    IPool? Find(string? name = null);

    /// <summary>Look up the pool a region was created on; <see langword="null"/> when missing.</summary>
    IPool? Find(IRegion region);

    /// <summary>Snapshot of the registry; free to mutate without affecting the manager.</summary>
    IReadOnlyDictionary<string, IPool> GetAll();
}
