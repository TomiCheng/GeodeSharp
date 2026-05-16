namespace Geode.Client;

/// <summary>
/// Non-generic region surface; key / value typed as <see cref="object"/>.
/// </summary>
public interface IRegion
{
    /// <summary>Region's local name (last segment of <see cref="FullPath"/>).</summary>
    string Name { get; }

    /// <summary>Name of the <see cref="IPool"/>
    /// this region was created on; empty when the cache's default pool is used.
    /// </summary>
    string PoolName { get; }

    /// <summary>Full path including parent regions (e.g. <c>"/orders"</c>).</summary>
    string FullPath { get; }

    /// <summary>Put <paramref name="value"/> under <paramref name="key"/> on the server.</summary>
    Task PutAsync(object key, object value, CancellationToken ct = default);

    /// <summary>Get the value under <paramref name="key"/>; <see langword="null"/> when the key is absent.</summary>
    Task<object?> GetAsync(object key, CancellationToken ct = default);

    /// <summary>Remove <paramref name="key"/>; returns <see langword="true"/> when the key existed.</summary>
    Task<bool> RemoveAsync(object key, CancellationToken ct = default);

    /// <summary>Check whether <paramref name="key"/> exists on the server.</summary>
    Task<bool> ContainsKeyAsync(object key, CancellationToken ct = default);

    /// <summary>Clear every entry from the region on the server (region itself stays).</summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>Invalidate <paramref name="key"/>
    /// on the server — the key stays, the value becomes <see langword="null"/>.
    /// </summary>
    Task InvalidateAsync(object key, CancellationToken ct = default);

    /// <summary>Remove every key in <paramref name="keys"/> from the region in one server roundtrip.</summary>
    Task RemoveAllAsync(IReadOnlyCollection<object> keys, CancellationToken ct = default);

    /// <summary>Put every entry in <paramref name="map"/> on the server in one roundtrip.</summary>
    Task PutAllAsync(IReadOnlyDictionary<object, object> map, CancellationToken ct = default);

    /// <summary>
    /// Fetch every key in <paramref name="keys"/> from the server in one roundtrip;
    /// server-missing keys appear with <see langword="null"/>.
    /// </summary>
    Task<IReadOnlyDictionary<object, object?>> GetAllAsync(
        IReadOnlyCollection<object> keys, CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> when at least one entry in the region satisfies
    /// the OQL <paramref name="predicate"/> (WHERE-clause only).
    /// </summary>
    Task<bool> ExistsValueAsync(string predicate, CancellationToken ct = default);

    /// <summary>
    /// Single-result OQL lookup: <see langword="null"/> when no match, the value when exactly one match,
    /// throws <see cref="GeodeException"/> when more than one.
    /// </summary>
    Task<object?> SelectValueAsync(string predicate, CancellationToken ct = default);
}

/// <summary>
/// Strongly-typed overlay on <see cref="IRegion"/>; <typeparamref name="TKey"/>
/// must implement <see cref="IEquatable{T}"/>, type mismatches surface as
/// <see cref="InvalidCastException"/>.
/// </summary>
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
    /// Fetch every key in <paramref name="keys"/> from the server in one roundtrip;
    /// server-missing keys are <b>absent</b> from the result
    /// (not present with <see langword="null"/>).
    /// </summary>
    Task<IReadOnlyDictionary<TKey, TValue?>> GetAllAsync(
        IReadOnlyCollection<TKey> keys, CancellationToken ct = default);

    /// <inheritdoc cref="IRegion.SelectValueAsync(string, CancellationToken)" />
    new Task<TValue?> SelectValueAsync(string predicate, CancellationToken ct = default);
}
