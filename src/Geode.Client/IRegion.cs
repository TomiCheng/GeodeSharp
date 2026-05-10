namespace Geode.Client;

/// <summary>
/// Non-generic <see cref="IRegion{TKey, TValue}"/> base. Mirrors
/// cppcache <c>Region</c> (<c>cppcache/include/geode/Region.hpp</c>);
/// the type-parameter split exists in C# only.
/// </summary>
public interface IRegion
{
    /// <summary>
    /// Name of the <see cref="IPool"/> this region was created on.
    /// Empty string if the region uses the cache's default pool.
    /// Mirrors cppcache <c>RegionAttributes::getPoolName()</c>
    /// (reachable via <c>region-&gt;getAttributes().getPoolName()</c>).
    /// </summary>
    string PoolName { get; }
}

public interface IRegion<TKey, TValue> : IRegion
    where TKey : notnull
{
}
