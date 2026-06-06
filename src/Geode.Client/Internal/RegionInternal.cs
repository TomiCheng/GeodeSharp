namespace Geode.Client.Internal;

/// <summary>
/// Abstract internal layer between the public <see cref="IRegion"/>
/// interface and the concrete region implementations
/// (<see cref="LocalRegion"/> &#x2192; <c>ThinClientRegion</c>).
/// Mirrors cppcache <c>RegionInternal</c>
/// (<c>cppcache/src/RegionInternal.hpp:131</c>).
/// </summary>
/// <remarks>
/// <para>
/// cppcache uses this layer to expose internal-only operations that
/// the public <c>Region</c> interface doesn't surface (event flags,
/// version tags, tombstones, internal Put / Get variants that take
/// <c>EventId</c> + <c>VersionTag</c>). All of those land in their
/// respective phases — Phase 1.x keeps the layer mostly empty so the
/// inheritance chain matches cppcache for future ports.
/// </para>
/// <para>
/// cppcache ctor takes <c>(CacheImpl*, RegionAttributes)</c>; the
/// cache back-pointer is deferred — first consumer that needs it
/// (likely the serialization registry in Phase 2 or stats in Phase
/// 1.5) will add it.
/// </para>
/// </remarks>
internal abstract class RegionInternal(RegionAttributes attributes)
    : IRegion, IAsyncDisposable
{
    /// <summary>
    /// Async teardown — mirrors cppcache <c>LocalRegion::release</c>
    /// (<c>cppcache/src/LocalRegion.cpp</c>). Default body is a no-op so
    /// region types without per-instance state inherit it for free;
    /// <see cref="LocalRegion"/> overrides to flush the entries map
    /// (which cascades into <c>LRUEntriesMap.DisposeAsync</c> →
    /// <see cref="EvictionController.UnregisterRegion"/>) and mark the
    /// region destroyed. Called from <c>GeodeCache.CloseAsync</c> before
    /// the pool drain, so cache-scoped DI services are still reachable.
    /// </summary>
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Post-construction async init, called by
    /// <c>GeodeCache.CreateRegionInternalAsync</c> after the region object
    /// exists. Default body is a no-op; <see cref="LocalRegion"/> overrides
    /// to wire the overflow-to-disk <see cref="IPersistenceManager"/> when
    /// <c>DiskPolicy == Overflows</c>. Distinct from
    /// <c>ThinClientRegion.InitTcrAsync</c>(DM 接線,subclass-specific)—
    /// 這條是 base region concern,所有 kind 都跑。
    /// </summary>
    /// <remarks>
    /// 失敗時 caller 負責 dispose 半成品(<see cref="DisposeAsync"/>)再
    /// rethrow:cppcache 靠 RAII 自動清,C# 得顯式補,因為 pm.InitAsync
    /// 可能已開 OS 資源(file handle)GC 管不到。
    /// </remarks>
    public virtual Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;


    /// <summary>
    /// Explicit-interface impl for <see cref="IRegion.Attributes"/> —
    /// surfaces the protected snapshot field without widening its access.
    /// </summary>
    RegionAttributes IRegion.Attributes => Attributes;

    /// <summary>
    /// Region attributes snapshot taken at <see cref="RegionFactory.CreateAsync"/>
    /// time. Mirrors cppcache <c>RegionInternal::m_regionAttributes</c>.
    /// </summary>
    protected internal RegionAttributes Attributes { get; } = attributes;

    public abstract Task ClearAsync(object? callback = null, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default);

    /// <inheritdoc />
    public abstract Task<bool> ContainsKeyOnServerAsync(object key, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual bool ContainsValueForKey(object key) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public abstract Task CreateAsync(object key, object? value, object? callbackArgument = null,
        CancellationToken ct = default);

    /// <inheritdoc />
    public virtual IRegion CreateSubregion(string name, RegionAttributes attributes) =>
        throw new NotImplementedException("Sub-regions are not yet implemented.");

    /// <inheritdoc />
    public abstract Task DestroyAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task DestroyRegionAsync(object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("DestroyRegionAsync is not yet wired (Phase 2+).");

    /// <inheritdoc />
    public virtual IReadOnlyList<IRegionEntry> Entries(bool recursive) =>
        throw new NotImplementedException("Local entry map not yet implemented.");
    public abstract Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default);
    public abstract Task<IReadOnlyDictionary<object, object?>> GetAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default);
    public abstract Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual IAttributesMutator GetAttributesMutator() =>
        throw new NotImplementedException("Attribute mutator not yet wired (Phase 2+).");

    /// <inheritdoc />
    public virtual IRegionEntry? GetEntry(object key) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    // ── interest-list / CQ subscription (cppcache Region.hpp:1082/1090/1135/1162/1194/1217/1256/1284) ─
    // NIE stubs. Depend on subscription channel (Phase 4+,
    // ThinClientHARegion's territory); body lands when that wiring does.

    /// <inheritdoc />
    public virtual IReadOnlyList<object> GetInterestList() =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual IReadOnlyList<string> GetInterestListRegex() =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual IRegion? GetSubregion(string path) =>
        throw new NotImplementedException("Sub-regions are not yet implemented.");
    public abstract Task InvalidateAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task InvalidateRegionAsync(object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("InvalidateRegionAsync is not yet wired (Phase 2+).");

    /// <inheritdoc />
    public virtual IReadOnlyList<object> Keys() =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual Task LocalClearAsync(object? callback = null, CancellationToken ct = default) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual void LocalCreate(object key, object value, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual void LocalDestroy(object key, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual void LocalDestroyRegion(object? callback = null) =>
        throw new NotImplementedException("Sub-regions are not yet implemented.");

    /// <inheritdoc />
    public virtual void LocalInvalidate(object key, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual void LocalInvalidateRegion(object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    // ── local-* mirrors (cppcache Region.hpp:448/575/656/746/927/982/222/165) ─
    // NIE stubs. Need a local entry map (Phase 2+ caching-enabled);
    // body lands on LocalRegion when m_entries arrives.

    /// <inheritdoc />
    public virtual void LocalPut(object key, object value, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual bool LocalRemove(object key, object value, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    /// <inheritdoc />
    public virtual bool LocalRemoveEx(object key, object? callback = null) =>
        throw new NotImplementedException("Local entry map not yet implemented.");
    public abstract Task PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback = null, CancellationToken ct = default);

    public abstract Task PutAsync(object key, object? value, object? callback = null, CancellationToken ct = default);
    public abstract Task<IReadOnlyList<object>> QueryAsync(string predicate, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task RegisterAllKeysAsync(
        bool isDurable = false,
        bool getInitialValues = false,
        bool receiveValues = true,
        CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual Task RegisterKeysAsync(
        IReadOnlyCollection<object> keys,
        bool isDurable = false,
        bool getInitialValues = false,
        bool receiveValues = true,
        CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual Task RegisterRegexAsync(
        string regex,
        bool isDurable = false,
        bool getInitialValues = false,
        bool receiveValues = true,
        CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");
    public abstract Task RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default);
    public abstract Task<bool> RemoveAsync(object key, object value, object? callback = null, CancellationToken ct = default);
    public abstract Task<bool> RemoveExAsync(object key, object? callback = null, CancellationToken ct = default);
    public abstract Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<object>> ServerKeysAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("ServerKeysAsync is not yet wired (Phase 2+).");

    /// <inheritdoc />
    public virtual IReadOnlyList<IRegion> Subregions(bool recursive) =>
        throw new NotImplementedException("Sub-regions are not yet implemented.");

    /// <inheritdoc />
    public virtual Task UnregisterAllKeysAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual Task UnregisterKeysAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual Task UnregisterRegexAsync(string regex, CancellationToken ct = default) =>
        throw new NotImplementedException("Subscription is not yet implemented.");

    /// <inheritdoc />
    public virtual IReadOnlyList<object?> Values() =>
        throw new NotImplementedException("Local entry map not yet implemented.");

    public abstract string FullPath { get; }

    /// <inheritdoc />
    public virtual bool IsDestroyed =>
        throw new NotImplementedException("Region lifecycle flag not yet wired (Phase 1.5).");

    // ── IRegion (forward to derived) ───────────────────────────
    public abstract string Name { get; }

    // ── Sub-region surface (cppcache Region.hpp:111/244/249/252/261) ─
    // NIE stubs. Concrete impls land with the sub-region feature
    // (Phase deferred); body moves down to LocalRegion when sub-region
    // tree state lands.

    /// <inheritdoc />
    public virtual IRegion? ParentRegion =>
        throw new NotImplementedException("Sub-regions are not yet implemented.");

    /// <inheritdoc />
    public abstract IPool? Pool { get; }

    /// <summary>
    /// Mirrors cppcache <c>RegionAttributes::getPoolName()</c>; resolved
    /// by <see cref="RegionFactory.CreateAsync"/> to either the
    /// caller-supplied pool name or the cache's default pool name.
    /// </summary>
    public string PoolName => Attributes.PoolName;

    /// <inheritdoc />
    public virtual IRegionService RegionService =>
        throw new NotImplementedException("Cache back-reference not yet wired (Phase 1.5).");

    /// <inheritdoc />
    public abstract int LocalCount { get; }

    /// <summary>
    /// Heap-LRU eviction hook — called by <see cref="EvictionController"/>
    /// 跨 region 派發。Mirrors cppcache
    /// <c>RegionInternal::evict(float)</c>
    /// (<c>cppcache/src/RegionInternal.hpp:255</c>, pure virtual);body 落在
    /// <see cref="LocalRegion"/>。cppcache 是 sync,我們 async-first 改 Task。
    /// </summary>
    internal abstract Task EvictAsync(float percentage, CancellationToken ct = default);

    // TODO future phases — internal-only API surface that cppcache
    // RegionInternal exposes; add as their respective phases ship:
    //   Phase 2+:  putNoThrow_remote / getNoThrow_remote (EventId-aware)
    //              versionStamp / tombstoneList / cacheImpl back-ref
    //   Phase 4:   single-hop / partitioned-region helpers
    //   Sub-region phase: createSubRegion / getSubRegion / subRegions
}
