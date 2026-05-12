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

    public RegionView(IRegion inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
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
        // Reference types: null stays null. Value types: unbox; null →
        // default(TValue). InvalidCastException surfaces here when the
        // stored value's runtime type doesn't unbox to TValue — the
        // caller is asking the wrong typed view for this region.
        return raw is null ? default : (TValue)raw;
    }

    public Task<bool> RemoveAsync(TKey key, CancellationToken ct = default)
        => _inner.RemoveAsync(key, ct);

    public Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default)
        => _inner.ContainsKeyAsync(key, ct);

    public Task ClearAsync(CancellationToken ct = default)
        => _inner.ClearAsync(ct);

    public Task InvalidateAsync(TKey key, CancellationToken ct = default)
        => _inner.InvalidateAsync(key!, ct);

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
}
