using Geode.Client.Protocol.Serialization;

namespace Geode.Client.Services;

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
internal sealed class RegionView<TKey, TValue> : IRegion<TKey, TValue>
    where TKey : IEquatable<TKey>
{
    private readonly IRegion _inner;
    private readonly TypedResultAdapter _adapter;

    public RegionView(IRegion inner, TypedResultAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(adapter);
        _inner = inner;
        _adapter = adapter;
    }

    // ── Metadata pass-through ──────────────────────────────────
    public string Name => _inner.Name;
    public string PoolName => _inner.PoolName;
    public string FullPath => _inner.FullPath;

    // ── Typed ops (the C# call-site shape) ─────────────────────
    public Task PutAsync(TKey key, TValue value, CancellationToken ct = default)
        => _inner.PutAsync(key, value!, ct);

    public async Task<TValue?> GetAsync(TKey key, CancellationToken ct = default)
    {
        var raw = await _inner.GetAsync(key, ct).ConfigureAwait(false);
        // Adapter reshapes wire-canonical containers (List<object?> from
        // CacheableArrayList, etc.) into the declared TValue form —
        // List<int>, IList<IList<string>>, int[], …. Scalars and
        // primitive arrays early-out unchanged via IsInstanceOfType.
        // Null in → default(TValue) out (matches .NET dictionary
        // conventions: missing reference value = null, missing value
        // type = zero). InvalidCastException surfaces here when the
        // stored value's shape genuinely doesn't fit TValue — caller
        // is asking the wrong typed view for this region.
        return _adapter.Convert<TValue>(raw);
    }

    public Task<bool> RemoveAsync(TKey key, CancellationToken ct = default)
        => _inner.RemoveAsync(key, ct);

    public Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default)
        => _inner.ContainsKeyAsync(key, ct);

    public Task ClearAsync(CancellationToken ct = default)
        => _inner.ClearAsync(ct);

    public Task InvalidateAsync(TKey key, CancellationToken ct = default)
        => _inner.InvalidateAsync(key!, ct);

    public Task RemoveAllAsync(IReadOnlyCollection<TKey> keys, CancellationToken ct = default)
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
        return _inner.RemoveAllAsync(boxed, ct);
    }

    public Task PutAllAsync(IReadOnlyDictionary<TKey, TValue> map, CancellationToken ct = default)
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
        return _inner.PutAllAsync(boxed, ct);
    }

    public async Task<IReadOnlyDictionary<TKey, TValue?>> GetAllAsync(
        IReadOnlyCollection<TKey> keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        // Box typed keys → object[]; the wire path is object-typed.
        var boxed = new object[keys.Count];
        var i = 0;
        foreach (var k in keys)
        {
            boxed[i++] = k!;
        }
        var raw = await _inner.GetAllAsync(boxed, ct).ConfigureAwait(false);

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
            typed[key] = _adapter.Convert<TValue>(kv.Value);
        }
        return typed;
    }

    // ── Object-typed ops (explicit interface — forward to inner) ──
    Task IRegion.PutAsync(object key, object value, CancellationToken ct)
        => _inner.PutAsync(key, value, ct);

    Task<object?> IRegion.GetAsync(object key, CancellationToken ct)
        => _inner.GetAsync(key, ct);

    Task<bool> IRegion.RemoveAsync(object key, CancellationToken ct)
        => _inner.RemoveAsync(key, ct);

    Task<bool> IRegion.ContainsKeyAsync(object key, CancellationToken ct)
        => _inner.ContainsKeyAsync(key, ct);

    Task IRegion.InvalidateAsync(object key, CancellationToken ct)
        => _inner.InvalidateAsync(key, ct);

    Task IRegion.RemoveAllAsync(IReadOnlyCollection<object> keys, CancellationToken ct)
        => _inner.RemoveAllAsync(keys, ct);

    Task IRegion.PutAllAsync(IReadOnlyDictionary<object, object> map, CancellationToken ct)
        => _inner.PutAllAsync(map, ct);

    Task<IReadOnlyDictionary<object, object?>> IRegion.GetAllAsync(
        IReadOnlyCollection<object> keys, CancellationToken ct)
        => _inner.GetAllAsync(keys, ct);
}
