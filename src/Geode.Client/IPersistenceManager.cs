using System.Collections.Generic;

namespace Geode.Client;

/// <summary>
/// User-implemented backing store for overflow-to-disk eviction: persists
/// entry values out of memory and reads them back on demand.
/// </summary>
public interface IPersistenceManager
{
    /// <summary>
    /// One-time setup after construction.
    /// </summary>
    ValueTask InitAsync(IRegion region, IReadOnlyDictionary<string, string>? diskProperties, CancellationToken ct = default);

    /// <summary>
    /// Persist <paramref name="value"/> for <paramref name="key"/>, returning the handle locating it.
    /// </summary>
    ValueTask<object?> WriteAsync(object key, object? value, object? persistenceInfo, CancellationToken ct = default);

    /// <summary>
    /// Persist all entries of the region.
    /// </summary>
    ValueTask<bool> WriteAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Read the value for <paramref name="key"/> back via its <paramref name="persistenceInfo"/> handle.
    /// </summary>
    ValueTask<object?> ReadAsync(object key, object? persistenceInfo, CancellationToken ct = default);

    /// <summary>
    /// Read all values for the region.
    /// </summary>
    ValueTask<bool> ReadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Remove the on-disk entry for <paramref name="key"/>.
    /// </summary>
    ValueTask DestroyAsync(object key, object? persistenceInfo, CancellationToken ct = default);

    /// <summary>
    /// Close / release the persistence manager.
    /// </summary>
    ValueTask CloseAsync(CancellationToken ct = default);
}
