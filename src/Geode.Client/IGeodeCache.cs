/*
using Geode.Client.Pdx;

namespace Geode.Client;

/// <summary>
/// A connection to a single Geode cluster, obtained from <see cref="IGeodeCacheFactory"/>.
/// </summary>
public interface IGeodeCache : IRegionService
{
    /// <summary>Logical name this cache was registered under; empty for the unnamed default.</summary>
    string Name { get; }

    /// <summary>PDX type registry for this cache.</summary>
    ITypeRegistry TypeRegistry { get; }

    /// <summary>
    /// Opens the connection and runs the handshake if not done yet; idempotent and optional (region/query/ping operations await it on first use).
    /// </summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the OQL query service for the given pool (<see langword="null"/> or empty selects <c>PoolManager.DefaultPool</c>).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="poolName"/> is supplied but no pool with that name is registered.</exception>
    /// <exception cref="InvalidOperationException">No default pool exists (cache not initialised, or all pools destroyed).</exception>
    IQueryService GetQueryService(string? poolName = null);

    /// <summary>Drop fields the local schema doesn't know about on read.</summary>
    bool PdxIgnoreUnreadFields { get; }

    /// <summary>Keep PDX values serialised on read.</summary>
    bool PdxReadSerialized { get; }
}

*/