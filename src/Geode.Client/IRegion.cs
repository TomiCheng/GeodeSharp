namespace Geode.Client;

/// <summary>
/// Non-generic region surface; the actual op methods live here with
/// <see cref="object"/>-typed key / value because XML-driven region
/// registration (Path A) doesn't carry <c>TKey</c> / <c>TValue</c>
/// information. Mirrors cppcache <c>Region</c>
/// (<c>cppcache/include/geode/Region.hpp</c>) — cppcache regions are
/// untyped at the native layer, only typed in the C++/CLI <c>clicache</c>
/// wrapper. The typed <see cref="IRegion{TKey, TValue}"/> overlay below
/// is the C# equivalent of the clicache wrapper.
/// </summary>
public interface IRegion
{
    /// <summary>Region's local name (last segment of <see cref="FullPath"/>).</summary>
    string Name { get; }

    /// <summary>
    /// Name of the <see cref="IPool"/> this region was created on.
    /// Empty string if the region uses the cache's default pool.
    /// Mirrors cppcache <c>RegionAttributes::getPoolName()</c>
    /// (reachable via <c>region-&gt;getAttributes().getPoolName()</c>).
    /// </summary>
    string PoolName { get; }

    /// <summary>
    /// Full path including parent regions (e.g. <c>"/orders"</c> for
    /// a root region, <c>"/parent/child"</c> for a sub-region). Mirrors
    /// cppcache <c>Region::getFullPath()</c>.
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// Put <paramref name="value"/> under <paramref name="key"/> on the
    /// server. Mirrors cppcache <c>Region::put(key, value)</c>.
    /// </summary>
    Task PutAsync(object key, object value, CancellationToken ct = default);

    /// <summary>
    /// Get the value under <paramref name="key"/>; <c>null</c> when the
    /// key is absent. Mirrors cppcache <c>Region::get(key)</c>.
    /// </summary>
    Task<object?> GetAsync(object key, CancellationToken ct = default);

    /// <summary>
    /// Remove <paramref name="key"/>; returns <c>true</c> when the key
    /// existed. Mirrors cppcache <c>Region::remove(key)</c>.
    /// </summary>
    Task<bool> RemoveAsync(object key, CancellationToken ct = default);

    /// <summary>
    /// Check whether <paramref name="key"/> exists on the server.
    /// Mirrors cppcache <c>Region::containsKeyOnServer(key)</c>.
    /// </summary>
    Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default);

    /// <summary>
    /// Clear every entry from the region on the server (region itself
    /// stays). Mirrors cppcache <c>Region::clear()</c>
    /// (<c>cppcache/include/geode/Region.hpp</c>) &#x2192;
    /// <c>ThinClientRegion::clearNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp</c>); wire is
    /// <c>MessageType.ClearRegion(36)</c>.
    /// </summary>
    /// <remarks>
    /// Server-driven only — there is no region-wide <c>InvalidateRegion</c>
    /// counterpart on the public surface (cppcache <c>InvalidateRegion(55)</c>
    /// is server-&#x2192;client notification, not a client op). Use
    /// <see cref="ClearAsync"/> when you want to drop all entries.
    /// </remarks>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalidate <paramref name="key"/> on the server &#x2014; the key
    /// stays, the value becomes <c>null</c>. Mirrors cppcache
    /// <c>Region::invalidate(key)</c> &#x2192;
    /// <c>ThinClientRegion::invalidateNoThrow_remote</c>; wire is
    /// <c>MessageType.Invalidate(83)</c>.
    /// </summary>
    /// <remarks>
    /// After invalidate, <see cref="ContainsKeyAsync"/> returns <c>true</c>
    /// and <see cref="GetAsync"/> returns <c>null</c> (until the next
    /// <see cref="PutAsync"/>). Missing-key behaviour is server-decided
    /// &#x2014; cppcache treats it as success; we mirror that contract.
    /// </remarks>
    Task InvalidateAsync(object key, CancellationToken ct = default);

    /// <summary>
    /// Remove every key in <paramref name="keys"/> from the region in
    /// one server roundtrip. Mirrors cppcache <c>Region::removeAll</c>
    /// (<c>cppcache/include/geode/Region.hpp</c>) &#x2192;
    /// <c>ThinClientRegion::multiHopRemoveAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1810-1863</c>); wire is
    /// <c>MessageType.RemoveAll(109)</c>.
    /// </summary>
    /// <remarks>
    /// Empty <paramref name="keys"/> is rejected (cppcache's per-key
    /// sequence-id reserve underflows on zero and the round-trip is a
    /// no-op anyway). Per-key missing-vs-removed reporting from the
    /// chunked reply is dropped on the floor in Phase 1.3 &#x2014; the
    /// op returns success once the server acks the batch; the
    /// versioned object-part list lands when client-side caching does
    /// (Phase 4+).
    /// </remarks>
    Task RemoveAllAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default);

    /// <summary>
    /// Put every entry in <paramref name="map"/> on the server in one
    /// roundtrip. Mirrors cppcache <c>Region::putAll</c>
    /// (<c>cppcache/include/geode/Region.hpp</c>) &#x2192;
    /// <c>ThinClientRegion::multiHopPutAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1476-1540</c>); wire is
    /// <c>MessageType.PutAll(56)</c>.
    /// </summary>
    /// <remarks>
    /// Empty <paramref name="map"/> is rejected (cppcache's per-entry
    /// sequence-id reserve underflows on zero). Per-key version tags
    /// from the chunked reply are dropped on the floor in Phase 1.3
    /// &#x2014; the op returns success once the server acks the batch;
    /// surfacing version info lands when client-side caching does
    /// (Phase 4+).
    /// </remarks>
    Task PutAllAsync(IReadOnlyDictionary<object, object> map, CancellationToken ct = default);

    /// <summary>
    /// Fetch every key in <paramref name="keys"/> from the server in
    /// one roundtrip. Returns a dictionary whose entry set is the
    /// caller-supplied keys; a key absent on the server appears with
    /// value <c>null</c> (cppcache parity &#x2014; misses are tagged
    /// with the per-entry miss flag <c>3</c> and value <c>null</c>).
    /// Mirrors cppcache <c>Region::getAll</c>
    /// (<c>cppcache/include/geode/Region.hpp</c>) &#x2192;
    /// <c>ThinClientRegion::getAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1089-1172</c>); wire is
    /// <c>MessageType.GetAll70(100)</c>.
    /// </summary>
    /// <remarks>
    /// Empty <paramref name="keys"/> is rejected. Phase 1.3 always
    /// requests deserialised values (cppcache <c>m_serializeValues</c>
    /// false); the raw-bytes overload is deferred. Per-key exception
    /// reporting (cppcache's <c>HashMapOfException</c>) is dropped in
    /// Phase 1.3 &#x2014; a server-side per-key exception surfaces
    /// as a top-level <see cref="GeodeException"/>; per-key surfacing
    /// lands when partial-result APIs do.
    /// </remarks>
    Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, CancellationToken ct = default);
}

/// <summary>
/// Strongly-typed wrapper over <see cref="IRegion"/>. <c>TKey</c> and
/// <c>TValue</c> are pure compile-time type guards — there is no
/// runtime <c>K,V</c> binding on the underlying region. Implementations
/// (see <c>Services.RegionView{TKey, TValue}</c>) box / unbox onto the
/// non-generic <see cref="IRegion"/> ops; type mismatches surface
/// naturally as <see cref="InvalidCastException"/> from the unbox.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key constraint <c>where TKey : IEquatable&lt;TKey&gt;</c></b>
/// is the .NET-side enforcement of cppcache's <c>CacheableKey</c>
/// requirement (<c>cppcache/include/geode/CacheableKey.hpp</c>) —
/// keys must declare equality so the server-side <c>equals</c> /
/// <c>hashCode</c> contract has a credible client-side counterpart.
/// All built-in scalar / <see cref="DateTime"/> / <see cref="string"/>
/// types satisfy this for free; <see cref="byte"/><c>[]</c> does
/// not (arrays use reference equality) — exactly mirroring cppcache
/// where <c>CacheableBytes</c> derives from
/// <c>DataSerializablePrimitive</c>, not <c>CacheableKey</c>. User
/// types (Phase 2 PDX) must implement <see cref="IEquatable{T}"/>
/// explicitly; <c>record</c> / <c>record struct</c> declarations get
/// it for free.
/// </para>
/// <para>
/// The constraint does <b>not</b> catch "TKey has no registered
/// codec" — that surfaces as <see cref="NotSupportedException"/>
/// from the serialisation registry at the first op call. Compile-
/// time vs runtime gap is acceptable: codec registration is dynamic
/// (DI scope), so a static check would over-restrict.
/// </para>
/// </remarks>
public interface IRegion<TKey, TValue> : IRegion
    where TKey : IEquatable<TKey>
{
    /// <inheritdoc cref="IRegion.PutAsync(object, object, CancellationToken)" />
    Task PutAsync(TKey key, TValue value, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.GetAsync(object, CancellationToken)" />
    Task<TValue?> GetAsync(TKey key, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.RemoveAsync(object, CancellationToken)" />
    Task<bool> RemoveAsync(TKey key, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.ContainsKeyAsync(object, CancellationToken)" />
    Task<bool> ContainsKeyAsync(TKey key, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.InvalidateAsync(object, CancellationToken)" />
    Task InvalidateAsync(TKey key, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.RemoveAllAsync(IReadOnlyCollection{object}, CancellationToken)" />
    Task RemoveAllAsync(IReadOnlyCollection<TKey> keys, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.PutAllAsync(IReadOnlyDictionary{object,object}, CancellationToken)" />
    Task PutAllAsync(IReadOnlyDictionary<TKey, TValue> map, CancellationToken ct = default);

    /// <summary>
    /// Fetch every key in <paramref name="keys"/> from the server in
    /// one roundtrip. The returned dictionary contains only the keys
    /// the server has values for &#x2014; server-missing keys are
    /// <b>absent</b> from the result (not present with
    /// <see langword="null"/>). Use
    /// <see cref="IReadOnlyDictionary{TKey, TValue}.TryGetValue"/> or
    /// <see cref="IReadOnlyDictionary{TKey, TValue}.ContainsKey"/> to
    /// detect missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Diverges from <see cref="IRegion.GetAllAsync(IReadOnlyCollection{object}, CancellationToken)"/>:
    /// the non-typed (raw-object) surface keeps cppcache parity
    /// &#x2014; missing keys appear with <see langword="null"/>
    /// because <see cref="object"/>? carries null directly. The
    /// typed surface can't do that uniformly &#x2014;
    /// <c>TValue?</c> for an unconstrained generic is a compile-time
    /// nullability annotation only, not <see cref="Nullable{T}"/>;
    /// for value-type <c>TValue</c> (e.g. <see cref="int"/>) a
    /// "null wire value" would collapse to <c>default(TValue)</c>
    /// and become indistinguishable from a legitimately-stored
    /// zero. Skipping missing keys at this layer keeps the
    /// observable contract unambiguous across reference and value
    /// types.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<TKey, TValue?>> GetAllAsync(
        IReadOnlyCollection<TKey> keys, CancellationToken ct = default);

    // No typed ClearAsync overload — the base IRegion.ClearAsync takes
    // no key / value, nothing to specialise.
}
