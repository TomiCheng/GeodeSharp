namespace Geode.Client;

/// <summary>
/// Common region / query lookup contract; implemented by <see cref="IGeodeCache"/>.
/// </summary>
public interface IRegionService : IAsyncDisposable
{
    /// <summary>Whether <see cref="CloseAsync"/> has been called.</summary>
    bool IsClosed { get; }

    /// <summary>Gracefully close the underlying connection(s); subsequent calls are a no-op.</summary>
    Task CloseAsync(CancellationToken ct = default);

    /// <summary>
    /// Get the strongly-typed handle for the region at <paramref name="path"/>;
    /// <see langword="null"/> when no region is registered there.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty or just <c>"/"</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The region exists but is already attached under different type parameters.
    /// </exception>
    IRegion<TKey, TValue>? GetRegion<TKey, TValue>(string path)
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Untyped lookup overload of <see cref="GetRegion{TKey, TValue}"/>;
    /// <see langword="null"/> when no region with <paramref name="path"/> is registered.
    /// </summary>
    IRegion? GetRegion(string path);

    // Phase 1.x: IReadOnlyList<IRegion> RootRegions { get; }
    // Phase 2:   PdxInstanceFactory CreatePdxInstanceFactory(string className, ...);
}
