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
internal class LocalRegion : RegionInternal
{

    static ObjectFactory<LocalRegion> _objectFactory
        = ActivatorUtilities.CreateFactory<LocalRegion>([typeof(string), typeof(RegionInternal), typeof(RegionAttributes)]);

    /// <summary>cppcache <c>m_attachedPool</c>: attached pool (we route via dm at <see cref="ThinClientRegion"/>).</summary>
    protected IPool? AttachedPool;

    /// <summary>cppcache <c>m_cacheStatistics</c>: per-region access / modification timestamps.</summary>
    protected object? CacheStatistics;

    /// <summary>cppcache <c>m_destroyPending</c>: region teardown in progress.</summary>
    protected bool DestroyPending;

    /// <summary>cppcache <c>m_enableTimeStatistics</c>: time-histogram flag (OTel always on; kept for parity).</summary>
    protected bool EnableTimeStatistics;

    /// <summary>cppcache <c>expiry_task_id_</c>: region-level ExpiryTask id.</summary>
    protected object? ExpiryTaskId;

    /// <summary>cppcache <c>m_isPRSingleHopEnabled</c>: single-hop routing for partitioned regions (Phase 4).</summary>
    protected bool IsPrSingleHopEnabled;

    /// <summary>cppcache <c>m_listener</c>: CacheListener (Phase 2+).</summary>
    protected object? Listener;

    /// <summary>cppcache <c>m_loader</c>: CacheLoader (Phase 2+).</summary>
    protected object? Loader;

    /// <summary>cppcache <c>m_entries</c> (<c>EntriesMap*</c>): local entry map (Phase 2+ caching-enabled). Renamed from cppcache's <c>m_entries</c> to (a) avoid clash with <see cref="IRegion.Entries(bool)"/> and (b) separate from the <see cref="EntriesMap"/> type name.</summary>
    protected Lazy<EntriesMap?> LocalEntriesMap;

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

    public LocalRegion(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes) : base(attributes)
    {
        Parent = parent;
        FullPath = parent is null ? "/" + name : parent.FullPath + "/" + name;
        Name = name;
        LocalEntriesMap = new Lazy<EntriesMap?>(() =>
        {
            if (attributes.CachingEnabled)
            {
                return EntriesMapFactory.CreateMap(serviceProvider, this, attributes);
            }
            return null;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Always-local entry count. The body of cppcache
    /// <c>LocalRegion::size_remote()</c>
    /// (<c>cppcache/src/LocalRegion.cpp:611-617</c>), exposed as a
    /// non-virtual helper so <see cref="LocalCount"/>'s no-tx branch can hit
    /// it directly — mirrors cppcache's <c>LocalRegion::size_remote()</c>
    /// explicit qualifier at <c>LocalRegion.cpp:628</c>.
    /// </summary>
    private int LocalSizeRemote()
    {
        // TODO Phase 1.5: CHECK_DESTROY_PENDING — cppcache
        //   LocalRegion.cpp:612 takes a shared_lock + checks destroy
        //   pending. Region-lifecycle flag still NIE (RegionInternal
        //   .IsDestroyed); add the guard when the flag lands.

        if (Attributes.CachingEnabled)
        {
            // cppcache LocalRegion.cpp:614 — m_entries->size().
            // LocalEntriesMap is allocated in the ctor via
            // EntriesMapFactory.CreateMap when CachingEnabled flips
            // true (Phase 2+); until that wiring lands the field is
            // still null so this dereference NREs before reaching the
            // NIE on EntriesMap.Count.
            return LocalEntriesMap.Value!.Count;
        }

        // Proxy / non-caching case — cppcache LocalRegion.cpp:616.
        return 0;
    }

    /// <summary>
    /// Parent region in the sub-region tree, or <see langword="null"/> for a
    /// root region. Mirrors cppcache <c>LocalRegion::m_parentRegion</c>.
    /// </summary>
    protected RegionInternal? Parent { get; }

    internal static LocalRegion Create(
        IServiceProvider serviceProvider,
        string name,
        RegionInternal? parent,
        RegionAttributes attributes)
    {
        return _objectFactory(serviceProvider, [name, parent, attributes]);
    }

    /// <summary>
    /// Current thread / async-flow's ambient transaction, or
    /// <see langword="null"/> when no tx is open. Mirrors cppcache
    /// <c>LocalRegion::getTXState()</c>
    /// (<c>cppcache/src/LocalRegion.hpp:477</c>), which delegates to
    /// <c>TSSTXStateWrapper::get().getTXState()</c>.
    /// </summary>
    internal TXState? GetTXState()
    {
        // TODO Phase 4+ (transactions): wire to TSSTXStateWrapper
        //   equivalent — likely a static AsyncLocal<TXState?> on a
        //   TSSTXStateWrapper helper, set by CacheTransactionManager
        //   .Begin / cleared by Commit / Rollback. Returning null today
        //   matches the "no transaction in progress" branch in every
        //   caller (e.g. LocalRegion::size line 619-629), so call sites
        //   can already reference this method without behavioural drift.
        return null;
    }

    /// <summary>
    /// Whether the current op is local-only — either because this region
    /// instance is a plain <see cref="LocalRegion"/> (no server backing)
    /// or because the caller flagged the op with
    /// <see cref="CacheEventFlags.Local"/>. Mirrors cppcache
    /// <c>LocalRegion::isLocalOp</c>
    /// (<c>cppcache/src/LocalRegion.hpp:482-485</c>).
    /// </summary>
    internal bool IsLocalOp(CacheEventFlags? eventFlags = null) =>
        // cppcache `typeid(*this) == typeid(LocalRegion)`: exact-type
        // (not derived) RTTI check. ThinClientRegion (and future
        // subclasses) carry a server, so they return false here.
        GetType() == typeof(LocalRegion)
        || (eventFlags is { } f && f.HasFlag(CacheEventFlags.Local));

    /// <summary>
    /// Virtual hook used by <see cref="LocalCount"/>'s in-tx branch. Default
    /// body matches the non-virtual <see cref="LocalSizeRemote"/> —
    /// <see cref="ThinClientRegion"/> overrides (Phase 1.5+) to round-trip
    /// <c>TcrMessageSize</c> to the server. Mirrors cppcache
    /// <c>LocalRegion::size_remote()</c> as the virtual dispatch target
    /// (called at <c>LocalRegion.cpp:625</c>).
    /// </summary>
    internal virtual int SizeRemote() => LocalSizeRemote();

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

    public override string FullPath { get; }

    public override string Name { get; }

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

    /// <summary>
    /// Mirrors cppcache <c>LocalRegion::size()</c>
    /// (<c>cppcache/src/LocalRegion.cpp:619-629</c>): tx-aware dispatch
    /// over the local entry count. Renamed from cppcache's <c>size()</c>
    /// to <c>LocalCount</c> in the C# port — see <see cref="IRegion.LocalCount"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache differentiates the two paths via a non-virtual call
    /// qualifier (<c>LocalRegion::size_remote()</c> at line 628) for the
    /// no-tx branch vs. a virtual call (<c>size_remote()</c> at line
    /// 625) for the in-tx branch. C# has no syntax to bypass virtual
    /// dispatch from <c>this</c>, so the body is split into a non-virtual
    /// helper (<see cref="LocalSizeRemote"/>) plus a virtual hook
    /// (<see cref="SizeRemote"/>) — the no-tx branch calls the helper
    /// directly, the in-tx branch goes through the virtual.
    /// </para>
    /// <para>
    /// We deliberately do <b>not</b> mirror cppcache's
    /// <c>return GF_NOTSUP;</c> on the tx + isLocalOp case
    /// (<c>LocalRegion.cpp:623</c>) — that returns the raw int 12 as
    /// though it were an entry count, which looks like a cppcache bug.
    /// We throw <see cref="NotSupportedException"/> instead.
    /// </para>
    /// </remarks>
    public override int LocalCount
    {
        get
        {
            var txState = GetTXState();
            if (txState is not null)
            {
                if (IsLocalOp())
                {
                    // cppcache LocalRegion.cpp:622-624 returns GF_NOTSUP
                    // as a uint count — we throw instead. Pure local
                    // region can't satisfy a tx (no server to coordinate
                    // with), so calling LocalCount in this combo is API misuse.
                    throw new NotSupportedException(
                        "Region.LocalCount: not supported on a local-only region inside a transaction.");
                }
                return SizeRemote();
            }
            return LocalSizeRemote();
        }
    }

}
