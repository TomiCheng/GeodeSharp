namespace Geode.Client;

/// <summary>
/// A connection to a single Geode cluster, obtained from <see cref="IGeodeCacheFactory"/>.
/// </summary>
public interface IGeodeCache //: IRegionService
{
    /// <summary>
    /// Cache name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Pool manager scoped to this cache.
    /// </summary>
    IPoolManager PoolManager { get; }

    ///// <summary>PDX type registry for this cache.</summary>
    //ITypeRegistry TypeRegistry { get; }



    ///// <summary>
    ///// Returns the OQL query service for the given pool (<see langword="null"/> or empty selects <c>PoolManager.DefaultPool</c>).
    ///// </summary>
    ///// <exception cref="ArgumentException"><paramref name="poolName"/> is supplied but no pool with that name is registered.</exception>
    ///// <exception cref="InvalidOperationException">No default pool exists (cache not initialised, or all pools destroyed).</exception>
    //IQueryService GetQueryService(string? poolName = null);

    ///// <summary>Drop fields the local schema doesn't know about on read.</summary>
    //bool PdxIgnoreUnreadFields { get; }

    ///// <summary>Keep PDX values serialised on read.</summary>
    //bool PdxReadSerialized { get; }
}


