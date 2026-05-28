
namespace Geode.Client;

/// <summary>
/// Non-generic region surface; key / value typed as <see cref="object"/>.
/// </summary>
public interface IRegion
{

    /// <summary>
    /// Clear every entry from the region on the server (region itself stays).
    /// </summary>
    Task ClearAsync(object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Local containment check — does the local cache hold <paramref name="key"/>?
    /// </summary>
    bool ContainsKey(object key);

    /// <summary>
    /// Check whether <paramref name="key"/> exists on the server.
    /// </summary>
    Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default);

    /// <summary>
    /// Local containment check for a non-invalidated value under <paramref name="key"/>.
    /// </summary>
    bool ContainsValueForKey(object key);

    /// <summary>
    /// Strict insert; throws when <paramref name="key"/> already exists (cppcache <c>Region::create</c>).
    /// </summary>
    Task CreateAsync(object key, object value, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Creates a sub-region named <paramref name="name"/> with <paramref name="attributes"/>.
    /// </summary>
    IRegion CreateSubregion(string name, RegionAttributes attributes);

    /// <summary>
    /// Strict remove; throws when <paramref name="key"/> is absent (cppcache <c>Region::destroy</c>).
    /// </summary>
    Task DestroyAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Destroys the whole region on the server (mirrors cppcache <c>Region::destroyRegion</c>).
    /// </summary>
    Task DestroyRegionAsync(object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Snapshot of <see cref="IRegionEntry"/> entries; descends into sub-regions
    /// when <paramref name="recursive"/> is <see langword="true"/>.
    /// </summary>
    IReadOnlyList<IRegionEntry> Entries(bool recursive);

    /// <summary>
    /// Returns <see langword="true"/> when at least one entry in the region satisfies
    /// the OQL <paramref name="predicate"/> (WHERE-clause only).
    /// </summary>
    Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default);

    /// <summary>
    /// Fetch every key in <paramref name="keys"/> from the server in one roundtrip;
    /// server-missing keys appear with <see langword="null"/>.
    /// </summary>
    Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Get the value under <paramref name="key"/>; <see langword="null"/> when the key is absent.
    /// </summary>
    Task<object?> GetAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Mutator for the subset of attributes adjustable after creation.
    /// </summary>
    IAttributesMutator GetAttributesMutator();

    /// <summary>
    /// Local <see cref="IRegionEntry"/> snapshot for <paramref name="key"/>; <see langword="null"/>
    /// when absent locally.
    /// </summary>
    IRegionEntry? GetEntry(object key);

    /// <summary>
    /// Keys this client has registered subscription interest in.
    /// </summary>
    IReadOnlyList<object> GetInterestList();

    /// <summary>
    /// Regular expressions this client has registered subscription interest in.
    /// </summary>
    IReadOnlyList<string> GetInterestListRegex();

    /// <summary>
    /// Returns the sub-region at <paramref name="path"/>, or <see langword="null"/> if absent.
    /// </summary>
    IRegion? GetSubregion(string path);

    /// <summary>Invalidate <paramref name="key"/>
    /// on the server — the key stays, the value becomes <see langword="null"/>.
    /// </summary>
    Task InvalidateAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Invalidates every entry in the region on the server (keys stay, values cleared).
    /// </summary>
    Task InvalidateRegionAsync(object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Snapshot of locally-cached keys.
    /// </summary>
    IReadOnlyList<object> Keys();

    /// <summary>
    /// Clears every entry from the local entry map only.
    /// </summary>
    void LocalClear(object? callback = null);

    /// <summary>
    /// Local-only strict insert; throws if <paramref name="key"/> already exists.
    /// </summary>
    void LocalCreate(object key, object value, object? callback = null);

    /// <summary>
    /// Local-only destroy of <paramref name="key"/>.
    /// </summary>
    void LocalDestroy(object key, object? callback = null);

    /// <summary>
    /// Destroys this region locally only (server stays unchanged); <paramref name="callback"/> reaches local <c>CacheWriter</c>/<c>CacheListener</c>.
    /// </summary>
    void LocalDestroyRegion(object? callback = null);

    /// <summary>
    /// Local-only invalidate (value cleared, key stays).
    /// </summary>
    void LocalInvalidate(object key, object? callback = null);

    /// <summary>
    /// Invalidates every entry's value locally (keys stay).
    /// </summary>
    void LocalInvalidateRegion(object? callback = null);

    /// <summary>
    /// Local-only put against the in-memory entry map (no server roundtrip).
    /// </summary>
    void LocalPut(object key, object value, object? callback = null);

    /// <summary>
    /// Local-only remove of <paramref name="key"/>/<paramref name="value"/>; <see langword="true"/> when removed.
    /// </summary>
    bool LocalRemove(object key, object value, object? callback = null);

    /// <summary>
    /// Local-only remove of <paramref name="key"/> ignoring value; <see langword="true"/> when removed.
    /// </summary>
    bool LocalRemoveEx(object key, object? callback = null);

    /// <summary>
    /// Put every entry in <paramref name="map"/> on the server in one roundtrip.
    /// </summary>
    Task PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Put <paramref name="value"/> under <paramref name="key"/> on the server.
    /// </summary>
    Task PutAsync(object key, object value, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Runs an OQL <paramref name="predicate"/> (WHERE-clause only, or a full
    /// <c>SELECT</c> / <c>IMPORT</c> statement) against this region and returns
    /// every matching row.
    /// </summary>
    Task<IReadOnlyList<object>> QueryAsync(string predicate, CancellationToken ct = default);

    /// <summary>
    /// Registers subscription interest in every key on the server. See <see cref="RegisterKeysAsync"/> for the flag semantics.
    /// </summary>
    Task RegisterAllKeysAsync(bool isDurable = false, bool getInitialValues = false, bool receiveValues = true,
        CancellationToken ct = default);

    /// <summary>Registers subscription interest in <paramref name="keys"/> with the server.</summary>
    /// <param name="keys">Keys to subscribe to.</param>
    /// <param name="isDurable">Keep the subscription queue alive across client reconnects.</param>
    /// <param name="getInitialValues">Request a full snapshot of current values for <paramref name="keys"/> right after registration.</param>
    /// <param name="receiveValues">Deliver values on update; when <see langword="false"/> the client receives only key-level notifications.</param>
    /// <param name="ct">Cooperative cancellation.</param>
    Task RegisterKeysAsync(IReadOnlyCollection<object> keys, bool isDurable = false, bool getInitialValues = false,
        bool receiveValues = true, CancellationToken ct = default);

    /// <summary>
    /// Registers subscription interest in keys matching <paramref name="regex"/>. See <see cref="RegisterKeysAsync"/> for the flag semantics.
    /// </summary>
    Task RegisterRegexAsync(
        string regex,
        bool isDurable = false,
        bool getInitialValues = false,
        bool receiveValues = true,
        CancellationToken ct = default);

    /// <summary>
    /// Remove every key in <paramref name="keys"/> from the region in one server roundtrip.
    /// </summary>
    Task RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Conditional remove: removes <paramref name="key"/> only when its current value
    /// equals <paramref name="value"/>; mirrors cppcache <c>Region::remove(key, value, cb)</c>.
    /// </summary>
    Task<bool> RemoveAsync(object key, object value, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Unconditional remove of <paramref name="key"/>; mirrors cppcache <c>Region::removeEx(key, cb)</c>.
    /// Returns <see langword="true"/> when the key existed.
    /// </summary>
    Task<bool> RemoveExAsync(object key, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Single-result OQL lookup: <see langword="null"/> when no match, the value when exactly one match,
    /// throws <see cref="GeodeException"/> when more than one.
    /// </summary>
    Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default);

    /// <summary>
    /// Snapshot of keys defined on the server for this region.
    /// </summary>
    Task<IReadOnlyList<object>> ServerKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Sub-regions immediately under this one (or transitively when <paramref name="recursive"/> is <see langword="true"/>).
    /// </summary>
    IReadOnlyList<IRegion> Subregions(bool recursive);

    /// <summary>
    /// Unregisters the all-keys subscription.
    /// </summary>
    Task UnregisterAllKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Unregisters previously-registered subscription interest in <paramref name="keys"/>.
    /// </summary>
    Task UnregisterKeysAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default);

    /// <summary>
    /// Unregisters the previously-registered <paramref name="regex"/>.
    /// </summary>
    Task UnregisterRegexAsync(string regex, CancellationToken ct = default);

    /// <summary>
    /// Snapshot of locally-cached values (invalidated entries skipped).
    /// </summary>
    IReadOnlyList<object?> Values();

    /// <summary>
    /// Attributes snapshot taken at region creation time.
    /// </summary>
    RegionAttributes Attributes { get; }

    /// <summary>
    /// Full path including parent regions (e.g. <c>"/orders"</c>).
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// Whether this region has been destroyed.
    /// </summary>
    bool IsDestroyed { get; }
    /// <summary>
    /// Region's local name (last segment of <see cref="FullPath"/>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Parent region; <see langword="null"/> at root.
    /// </summary>
    IRegion? ParentRegion { get; }

    /// <summary>
    /// Live <see cref="IPool"/> this region dispatches through.
    /// </summary>
    IPool Pool { get; }

    /// <summary>Name of the <see cref="IPool"/>
    /// this region was created on; empty when the cache's default pool is used.
    /// </summary>
    string PoolName { get; }

    /// <summary>
    /// Parent <see cref="IRegionService"/> (the cache) this region belongs to.
    /// </summary>
    IRegionService RegionService { get; }

    /// <summary>
    /// Number of entries currently in the local cache. <b>Local only</b> —
    /// in proxy mode this returns 0 even when the server holds entries;
    /// use <see cref="ServerKeysAsync"/> for the server-side count.
    /// Mirrors cppcache <c>Region::size()</c>.
    /// </summary>
    int LocalCount { get; }

}

/// <summary>
/// Strongly-typed overlay on <see cref="IRegion"/>; <typeparamref name="TKey"/>
/// must implement <see cref="IEquatable{T}"/>, type mismatches surface as
/// <see cref="InvalidCastException"/>.
/// </summary>
public interface IRegion<TKey, TValue> : IRegion
    where TKey : IEquatable<TKey>
{

    /// <inheritdoc cref="IRegion.ContainsKey(object)" />
    bool ContainsKey(TKey key);

    /// <inheritdoc cref="IRegion.ContainsKeyAsync(object, CancellationToken)" />
    Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.ContainsValueForKey(object)" />
    bool ContainsValueForKey(TKey key);

    /// <inheritdoc cref="IRegion.CreateAsync(object, object, object?, CancellationToken)" />
    Task CreateAsync(TKey key, TValue value, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.DestroyAsync(object, object?, CancellationToken)" />
    Task DestroyAsync(TKey key, object? callback = null, CancellationToken ct = default);

    /// <summary>
    /// Fetch every key in <paramref name="keys"/> from the server in one roundtrip;
    /// server-missing keys are <b>absent</b> from the result
    /// (not present with <see langword="null"/>).
    /// </summary>
    Task<IReadOnlyDictionary<TKey, TValue?>> GetAllAsync(
        IReadOnlyCollection<TKey> keys, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.GetAsync(object, object?, CancellationToken)" />
    Task<TValue?> GetAsync(TKey key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.GetEntry(object)" />
    IRegionEntry? GetEntry(TKey key);

    /// <inheritdoc cref="IRegion.InvalidateAsync(object, object?, CancellationToken)" />
    Task InvalidateAsync(TKey key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.Keys" />
    new IReadOnlyList<TKey> Keys();

    /// <inheritdoc cref="IRegion.PutAllAsync(IReadOnlyDictionary{object,object}, object?, CancellationToken)" />
    Task PutAllAsync(IReadOnlyDictionary<TKey, TValue> map, object? callback = null, CancellationToken ct = default);
    /// <inheritdoc cref="IRegion.PutAsync(object, object, object?, CancellationToken)" />
    Task PutAsync(TKey key, TValue value, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.RemoveAllAsync(IReadOnlyCollection{object}, object?, CancellationToken)" />
    Task RemoveAllAsync(IReadOnlyCollection<TKey> keys, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.RemoveAsync(object, object, object?, CancellationToken)" />
    Task<bool> RemoveAsync(TKey key, TValue value, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.RemoveExAsync(object, object?, CancellationToken)" />
    Task<bool> RemoveExAsync(TKey key, object? callback = null, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.SelectValueAsync(string, CancellationToken)" />
    new Task<TValue?> SelectValueAsync(string predicate, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.Values" />
    new IReadOnlyList<TValue?> Values();

}
