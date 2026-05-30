using Geode.Client.Services;

namespace Geode.Client.Internal;

/// <summary>
/// Compile-time-only typed view over a non-generic <see cref="IRegion"/>.
/// Returned by <see cref="Cache.GetRegion{TKey, TValue}(string)"/>; one
/// fresh instance per call (cheap throwaway). No cppcache analogue —
/// cppcache native <c>Region</c> is non-generic, and the C++/CLI
/// <c>clicache</c> typed wrapper sits at a different layer (the public
/// API). C# folds both layers into one: the non-generic
/// <see cref="IRegion"/> carries the wire path; this <see cref="IRegion{TKey, TValue}"/>
/// implementation just adds compile-time type guards.
/// </summary>
/// <remarks>
/// <para>
/// <b>No runtime type binding.</b> A region can be viewed under any
/// <c>&lt;TKey, TValue&gt;</c> pair the caller picks — the wrapper
/// boxes the typed args into <see cref="object"/> and forwards. Wrong
/// types surface as <see cref="InvalidCastException"/> from the unbox
/// inside <see cref="GetAsync"/>, never as a "type already bound"
/// error.
/// </para>
/// <para>
/// <b>Not cached.</b> Each <c>GetRegion&lt;K,V&gt;(name)</c> call
/// allocates a fresh wrapper. The wrapper holds one reference and
/// nothing else, so allocation cost is negligible; if a profile ever
/// disagrees, add a <c>ConditionalWeakTable</c> on
/// <see cref="Services.Cache"/> keyed by the inner region.
/// </para>
/// </remarks>
internal sealed class RegionView<TKey, TValue>(IRegion inner, TypedResultAdapter adapter)
    : IRegion<TKey, TValue>
    where TKey : IEquatable<TKey>
{

    // ── Metadata pass-through ──────────────────────────────────
    public string Name => inner.Name;
    public string PoolName => inner.PoolName;
    public string FullPath => inner.FullPath;
    public IPool Pool => inner.Pool;
    public IRegion? ParentRegion => inner.ParentRegion;
    public IRegion? GetSubregion(string path) => inner.GetSubregion(path);
    public IReadOnlyList<IRegion> Subregions(bool recursive) => inner.Subregions(recursive);
    public void LocalDestroyRegion(object? callback = null) => inner.LocalDestroyRegion(callback);
    public void LocalPut(object key, object value, object? callback = null) => inner.LocalPut(key, value, callback);
    public void LocalCreate(object key, object value, object? callback = null) => inner.LocalCreate(key, value, callback);
    public void LocalInvalidate(object key, object? callback = null) => inner.LocalInvalidate(key, callback);
    public void LocalDestroy(object key, object? callback = null) => inner.LocalDestroy(key, callback);
    public bool LocalRemove(object key, object value, object? callback = null) => inner.LocalRemove(key, value, callback);
    public bool LocalRemoveEx(object key, object? callback = null) => inner.LocalRemoveEx(key, callback);
    public void LocalClear(object? callback = null) => inner.LocalClear(callback);
    public void LocalInvalidateRegion(object? callback = null) => inner.LocalInvalidateRegion(callback);
    public IReadOnlyList<object> GetInterestList() => inner.GetInterestList();
    public IReadOnlyList<string> GetInterestListRegex() => inner.GetInterestListRegex();
    public Task RegisterKeysAsync(IReadOnlyCollection<object> keys, bool isDurable = false, bool getInitialValues = false, bool receiveValues = true, CancellationToken ct = default) => inner.RegisterKeysAsync(keys, isDurable, getInitialValues, receiveValues, ct);
    public Task UnregisterKeysAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default) => inner.UnregisterKeysAsync(keys, ct);
    public Task RegisterAllKeysAsync(bool isDurable = false, bool getInitialValues = false, bool receiveValues = true, CancellationToken ct = default) => inner.RegisterAllKeysAsync(isDurable, getInitialValues, receiveValues, ct);
    public Task UnregisterAllKeysAsync(CancellationToken ct = default) => inner.UnregisterAllKeysAsync(ct);
    public Task RegisterRegexAsync(string regex, bool isDurable = false, bool getInitialValues = false, bool receiveValues = true, CancellationToken ct = default) => inner.RegisterRegexAsync(regex, isDurable, getInitialValues, receiveValues, ct);
    public Task UnregisterRegexAsync(string regex, CancellationToken ct = default) => inner.UnregisterRegexAsync(regex, ct);
    public Task<IReadOnlyList<object>> QueryAsync(string predicate, CancellationToken ct = default) => inner.QueryAsync(predicate, ct);
    public IRegion CreateSubregion(string name, RegionAttributes attributes) => inner.CreateSubregion(name, attributes);

    // ── Typed ops (the C# call-site shape) ─────────────────────
    public Task PutAsync(TKey key, TValue? value, object? callback = null, CancellationToken ct = default)
        => inner.PutAsync(key, value, callback, ct);

    public async Task<TValue?> GetAsync(TKey key, object? callback = null, CancellationToken ct = default)
    {
        var raw = await inner.GetAsync(key, callback, ct).ConfigureAwait(false);
        // Adapter reshapes wire-canonical containers (List<object?> from
        // CacheableArrayList, etc.) into the declared TValue form —
        // List<int>, IList<IList<string>>, int[], …. Scalars and
        // primitive arrays early-out unchanged via IsInstanceOfType.
        // Null in → default(TValue) out (matches .NET dictionary
        // conventions: missing reference value = null, missing value
        // type = zero). InvalidCastException surfaces here when the
        // stored value's shape genuinely doesn't fit TValue — caller
        // is asking the wrong typed view for this region.
        return adapter.Convert<TValue>(raw);
    }

    public Task<bool> RemoveAsync(TKey key, TValue value, object? callback = null, CancellationToken ct = default)
        => inner.RemoveAsync(key!, value!, callback, ct);

    public Task<bool> RemoveExAsync(TKey key, object? callback = null, CancellationToken ct = default)
        => inner.RemoveExAsync(key!, callback, ct);

    public Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default)
        => inner.ContainsKeyAsync(key!, ct);

    public Task<bool> ContainsKeyOnServerAsync(TKey key, CancellationToken ct = default)
        => inner.ContainsKeyOnServerAsync(key!, ct);

    public Task ClearAsync(object? callback = null, CancellationToken ct = default)
        => inner.ClearAsync(callback, ct);

    public Task InvalidateAsync(TKey key, object? callback = null, CancellationToken ct = default)
        => inner.InvalidateAsync(key!, callback, ct);

    // No alias needed — bool return doesn't depend on TValue, the base
    // IRegion's ExistsValueAsync member satisfies the inherited contract
    // and is picked up by the typed view automatically.
    public Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default)
        => inner.ExistsValueAsync(predicate, ct);

    public async Task<TValue?> SelectValueAsync(string predicate, CancellationToken ct = default)
    {
        var raw = await inner.SelectValueAsync(predicate, ct).ConfigureAwait(false);
        // Same adapter path as GetAsync: scalar/array shortcut via
        // IsInstanceOfType, otherwise reshape wire-canonical containers
        // (List<object?>, etc.) into TValue. Null in → default(TValue?).
        return adapter.Convert<TValue>(raw);
    }

    public Task RemoveAllAsync(IReadOnlyCollection<TKey> keys, object? callback = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        // Box typed keys to object[] and forward; the inner region is
        // non-generic so we can't pass the typed collection straight
        // through. New array per call — bulk ops are not on the
        // allocation-critical path.
        var boxed = new object[keys.Count];
        var i = 0;
        foreach (var k in keys)
        {
            boxed[i++] = k!;
        }
        return inner.RemoveAllAsync(boxed, callback, ct);
    }

    public Task PutAllAsync(IReadOnlyDictionary<TKey, TValue> map, object? callback = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        // Box typed entries into a fresh Dictionary<object, object>;
        // the inner region is non-generic so the typed dict can't ride
        // through (covariance doesn't apply to IReadOnlyDictionary).
        // Allocation matches RemoveAllAsync — bulk ops aren't on the
        // hot path.
        var boxed = new Dictionary<object, object>(map.Count);
        foreach (var kv in map)
        {
            boxed[kv.Key!] = kv.Value!;
        }
        return inner.PutAllAsync(boxed, callback, ct);
    }

    public async Task<IReadOnlyDictionary<TKey, TValue?>> GetAllAsync(
        IReadOnlyCollection<TKey> keys, object? callback = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        // Box typed keys → object[]; the wire path is object-typed.
        var boxed = new object[keys.Count];
        var i = 0;
        foreach (var k in keys)
        {
            boxed[i++] = k!;
        }
        var raw = await inner.GetAllAsync(boxed, callback, ct).ConfigureAwait(false);

        // Reshape Dictionary<object, object?> → Dictionary<TKey, TValue?>
        // via TypedResultAdapter — same recursive-descent path that
        // GetAsync uses for nested generics. Each value goes through
        // Convert<TValue>(raw) so a region declared
        // <int, IList<int>> still gets List<object?> → List<int>
        // reshaping per entry; missing-key entries (null value) collapse
        // to default(TValue?).
        var typed = new Dictionary<TKey, TValue?>(raw.Count);
        foreach (var kv in raw)
        {
            // Skip server-missing entries at the typed boundary.
            //
            // The non-typed inner layer (cppcache parity) keeps null
            // values to represent "key not on server" — that works
            // there because the value slot is object?. On the typed
            // layer the result type is IReadOnlyDictionary<TKey,
            // TValue?> but TValue? for an unconstrained generic is NOT
            // Nullable<TValue> at runtime (only a compile-time
            // nullability annotation), so for value-type TValue
            // (e.g. int) a "null wire value" would collapse to
            // default(TValue)=0 and become indistinguishable from a
            // legitimately-stored 0. .NET idiom for "absent key" is
            // dict.ContainsKey/TryGetValue returning false; skipping
            // the null here makes the typed surface unambiguous and
            // matches the XML-doc contract on
            // <see cref="IRegion{TKey,TValue}.GetAllAsync"/>.
            //
            // Safe because Phase 1.3 PutAsync / PutAllAsync both
            // ArgumentNullException-guard the value — a region never
            // stores a null value legitimately, so null on the wire
            // is always the cppcache miss-flag-3 sentinel.
            if (kv.Value is null)
            {
                continue;
            }

            // Key reshape is straightforward — inner Keys mirror what we
            // passed in (we sent object-boxed TKey, server echoes none —
            // ChunkedGetAllResponse threads our original keys back into
            // the result dict), so a direct cast holds. The "!" silences
            // CS8600 for the K reference-type case; if it ever fails the
            // InvalidCastException is the right surface (wrong typed view).
            var key = (TKey)kv.Key;
            typed[key] = adapter.Convert<TValue>(kv.Value);
        }
        return typed;
    }

    // ── Object-typed ops (explicit interface — forward to inner) ──
    Task IRegion.PutAsync(object key, object? value, object? callback, CancellationToken ct)
        => inner.PutAsync(key, value, callback, ct);

    Task<object?> IRegion.GetAsync(object key, object? callback, CancellationToken ct)
        => inner.GetAsync(key, callback, ct);

    Task<bool> IRegion.RemoveAsync(object key, object value, object? callback, CancellationToken ct)
        => inner.RemoveAsync(key, value, callback, ct);

    Task<bool> IRegion.RemoveExAsync(object key, object? callback, CancellationToken ct)
        => inner.RemoveExAsync(key, callback, ct);

    Task<bool> IRegion.ContainsKeyAsync(object key, CancellationToken ct)
        => inner.ContainsKeyAsync(key, ct);

    Task<bool> IRegion.ContainsKeyOnServerAsync(object key, CancellationToken ct)
        => inner.ContainsKeyOnServerAsync(key, ct);

    Task IRegion.InvalidateAsync(object key, object? callback, CancellationToken ct)
        => inner.InvalidateAsync(key, callback, ct);

    Task IRegion.RemoveAllAsync(IReadOnlyCollection<object> keys, object? callback, CancellationToken ct)
        => inner.RemoveAllAsync(keys, callback, ct);

    Task IRegion.PutAllAsync(IReadOnlyDictionary<object, object> map, object? callback, CancellationToken ct)
        => inner.PutAllAsync(map, callback, ct);

    Task<IReadOnlyDictionary<object, object?>> IRegion.GetAllAsync(
        IReadOnlyCollection<object> keys, object? callback, CancellationToken ct)
        => inner.GetAllAsync(keys, callback, ct);

    // Explicit-interface overload for the object-typed SelectValueAsync;
    // the typed implicit member above shadows the base via `new`, so
    // calls through an IRegion reference need this explicit forwarder
    // to skip the adapter and return the raw object?.
    Task<object?> IRegion.SelectValueAsync(string predicate, CancellationToken ct)
        => inner.SelectValueAsync(predicate, ct);

    // ── Phase 2+ NIE-stub surface (forward through to inner) ───
    // Typed pairs follow the Put/Get/Remove pattern: typed overload
    // boxes into the non-generic inner call.

    public Task CreateAsync(TKey key, TValue value, object? callback = null, CancellationToken ct = default)
        => inner.CreateAsync(key!, value!, callback, ct);

    public Task DestroyAsync(TKey key, object? callback = null, CancellationToken ct = default)
        => inner.DestroyAsync(key!, callback, ct);

    public IRegionEntry? GetEntry(TKey key) => inner.GetEntry(key!);

    public bool ContainsValueForKey(TKey key) => inner.ContainsValueForKey(key!);

    public IReadOnlyList<TKey> Keys()
    {
        // Typed shadow — boxed inner snapshot → typed list via cast.
        // Wrong-type entries surface as InvalidCastException, matching
        // the GetAsync adapter's contract for "you asked the wrong typed view".
        var raw = inner.Keys();
        var typed = new TKey[raw.Count];
        for (var i = 0; i < raw.Count; i++) typed[i] = (TKey)raw[i];
        return typed;
    }

    public IReadOnlyList<TValue?> Values()
    {
        // Same boxed→typed shape as Keys; null values stay null via default.
        var raw = inner.Values();
        var typed = new TValue?[raw.Count];
        for (var i = 0; i < raw.Count; i++)
            typed[i] = adapter.Convert<TValue>(raw[i]);
        return typed;
    }

    // Object-typed explicit forwarders so an IRegion reference sees the
    // base surface unchanged (the typed Keys/Values above shadow via `new`).
    Task IRegion.CreateAsync(object key, object value, object? callback, CancellationToken ct)
        => inner.CreateAsync(key, value, callback, ct);
    Task IRegion.DestroyAsync(object key, object? callback, CancellationToken ct)
        => inner.DestroyAsync(key, callback, ct);
    Task IRegion.DestroyRegionAsync(object? callback, CancellationToken ct)
        => inner.DestroyRegionAsync(callback, ct);
    Task IRegion.InvalidateRegionAsync(object? callback, CancellationToken ct)
        => inner.InvalidateRegionAsync(callback, ct);
    IRegionEntry? IRegion.GetEntry(object key) => inner.GetEntry(key);
    IReadOnlyList<object> IRegion.Keys() => inner.Keys();
    Task<IReadOnlyList<object>> IRegion.ServerKeysAsync(CancellationToken ct) => inner.ServerKeysAsync(ct);
    IReadOnlyList<object?> IRegion.Values() => inner.Values();
    IReadOnlyList<IRegionEntry> IRegion.Entries(bool recursive) => inner.Entries(recursive);
    int IRegion.LocalCount => inner.LocalCount;
    bool IRegion.IsDestroyed => inner.IsDestroyed;
    RegionAttributes IRegion.Attributes => inner.Attributes;
    IAttributesMutator IRegion.GetAttributesMutator() => inner.GetAttributesMutator();
    bool IRegion.ContainsValueForKey(object key) => inner.ContainsValueForKey(key);
    IRegionService IRegion.RegionService => inner.RegionService;
}
