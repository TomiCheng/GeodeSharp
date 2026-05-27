using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract local-only region machinery. Mirrors cppcache
/// <c>LocalRegion</c> (<c>cppcache/src/LocalRegion.hpp:119</c>) — owns
/// the in-memory entry map (<c>m_entries</c>), name / full-path,
/// listener / writer / loader hooks, persistence manager, expiry
/// task plumbing.
/// </summary>
/// <remarks>
/// <para>
/// Phase 1.x is proxy-only (no client-side caching), so the in-memory
/// map + callback machinery is all deferred. The class still exists in
/// the hierarchy so <c>ThinClientRegion</c> sits at the same depth as
/// cppcache; once <c>caching-enabled</c> is honoured (Phase 2+), the
/// local-cache code lands here without disturbing the derived class.
/// </para>
/// <para>
/// cppcache ctor signature: <c>(name, CacheImpl*, parentRegion,
/// RegionAttributes, CacheStatistics, enableTimeStatistics)</c>. We
/// keep <c>name</c> + <c>parent</c> + <c>attributes</c>; cache back-ref,
/// stats, and time-stats flag are deferred until something actually
/// reads them.
/// </para>
/// </remarks>
internal class LocalRegion(
    IServiceProvider serviceProvider,
    string name,
    RegionInternal? parent,
    RegionAttributes attributes) : RegionInternal(attributes)
{
    IServiceProvider _ = serviceProvider;

    static ObjectFactory<LocalRegion> _objectFactory
        = ActivatorUtilities.CreateFactory<LocalRegion>([typeof(string), typeof(RegionInternal), typeof(RegionAttributes)]);

    internal static LocalRegion Create(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes)
    {
        return _objectFactory(serviceProvider, [name, parent, attributes]);
    }

    /// <summary>cppcache <c>m_attachedPool</c>: attached pool (we route via dm at <see cref="ThinClientRegion"/>).</summary>
    protected IPool? AttachedPool;

    /// <summary>cppcache <c>m_cacheStatistics</c>: per-region access / modification timestamps.</summary>
    protected object? CacheStatistics;

    /// <summary>cppcache <c>m_destroyPending</c>: region teardown in progress.</summary>
    protected bool DestroyPending;

    /// <summary>cppcache <c>m_enableTimeStatistics</c>: time-histogram flag (OTel always on; kept for parity).</summary>
    protected bool EnableTimeStatistics;

    /// <summary>cppcache <c>m_entries</c> (<c>EntriesMap*</c>): local entry map (Phase 2+ caching-enabled). Renamed from cppcache's <c>m_entries</c> to avoid clash with <see cref="IRegion.Entries(bool)"/>.</summary>
    protected object? EntriesMap;

    /// <summary>cppcache <c>expiry_task_id_</c>: region-level ExpiryTask id.</summary>
    protected object? ExpiryTaskId;

    /// <summary>cppcache <c>m_isPRSingleHopEnabled</c>: single-hop routing for partitioned regions (Phase 4).</summary>
    protected bool IsPrSingleHopEnabled;

    /// <summary>cppcache <c>m_listener</c>: CacheListener (Phase 2+).</summary>
    protected object? Listener;

    /// <summary>cppcache <c>m_loader</c>: CacheLoader (Phase 2+).</summary>
    protected object? Loader;

    /// <summary>cppcache <c>mutex_</c>: region-wide reader-writer lock (boost::shared_mutex).</summary>
    protected object? Mutex;

    /// <summary>cppcache <c>m_persistenceManager</c>: PersistenceManager (CLAUDE.md «Not implemented»).</summary>
    protected object? PersistenceManager;

    /// <summary>cppcache <c>m_regionStats</c>: per-region Meter sink.</summary>
    protected RegionStatistics? RegionStats;

    /// <summary>cppcache <c>m_released</c>: dispose path completed.</summary>
    protected bool Released;

    // ── cppcache LocalRegion fields (LocalRegion.hpp:511-530, 564) ─
    // Mirror every cppcache LocalRegion member here so the surface
    // matches 1:1. Types we have a C# port for use the real type
    // (RegionStatistics / IPool); everything else parks as `object?`
    // until the feature ships. Bool flags carry their cppcache default.
    // None of these are read today — they exist so future feature
    // ports (caching, expiry, listener, tombstone, single-hop, …)
    // land as body fills against the already-present field set.

    /// <summary>cppcache <c>m_subRegions</c>: synchronized_map name → sub-region.</summary>
    protected object? SubRegions;

    /// <summary>cppcache <c>m_tombstoneList</c>: CRDT tombstone tracking.</summary>
    protected object? TombstoneList;

    /// <summary>cppcache <c>m_transactionEnabled</c>: TX support flag.</summary>
    protected bool TransactionEnabled;

    /// <summary>cppcache <c>m_writer</c>: CacheWriter (Phase 2+).</summary>
    protected object? Writer;

    /// <summary>
    /// Parent region in the sub-region tree, or <see langword="null"/> for a
    /// root region. Mirrors cppcache <c>LocalRegion::m_parentRegion</c>.
    /// </summary>
    protected RegionInternal? Parent { get; } = parent;

    public override string FullPath { get; } = parent is null
            ? "/" + name
            : parent.FullPath + "/" + name;

    public override string Name { get; } = name;

    /// <summary>
    /// Local entry count. Mirrors cppcache <c>LocalRegion::size()</c>
    /// (<c>LocalRegion.cpp</c> via <c>m_entries->size()</c>). We're
    /// proxy-only today (<see cref="EntriesMap"/> stays null), so the
    /// local entry count is always 0 — that's the truthful answer for
    /// a non-caching region. Override on a future caching-enabled
    /// subclass when <see cref="EntriesMap"/> is materialised.
    /// </summary>
    public override int Size => 0;

    // ── Abstract RegionInternal members satisfied as NIE ───────
    // Mirrors cppcache LocalRegion being concrete: every IRegion op
    // has a "local default" sitting at this layer; ThinClientRegion
    // overrides them with wire-bound bodies. Until the local entry-map
    // (EntriesMap) ships we throw NotImplementedException — when
    // caching-enabled lands, these bodies switch to consulting
    // EntriesMap (cppcache LocalRegion.cpp:getNoThrow / putNoThrow
    // template path) and only fall through to a derived hook for the
    // network leg.

    /// <inheritdoc />
    public override IPool Pool =>
        throw new NotImplementedException("LocalRegion has no attached pool; ThinClientRegion override carries it.");

    /// <inheritdoc />
    public override Task ClearAsync(object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ClearAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ContainsKeyAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.ExistsValueAsync: pending OQL routing through ThinClientRegion override.");

    /// <inheritdoc />
    public override Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.GetAllAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.GetAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task InvalidateAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.InvalidateAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.PutAllAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task PutAsync(object key, object value, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.PutAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<IReadOnlyList<object>> QueryAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.QueryAsync: pending OQL routing through ThinClientRegion override.");

    /// <inheritdoc />
    public override Task RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveAllAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> RemoveAsync(object key, object value, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveAsync (strict): pending local entry map.");

    /// <inheritdoc />
    public override Task<bool> RemoveExAsync(object key, object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.RemoveExAsync: pending local entry map.");

    /// <inheritdoc />
    public override Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default) =>
        throw new NotImplementedException("LocalRegion.SelectValueAsync: pending OQL routing through ThinClientRegion override.");
}
