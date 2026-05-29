using System.Collections.Generic;

namespace Geode.Client;

/// <summary>
/// User-implemented backing store for overflow-to-disk eviction: persists
/// entry values out of memory and reads them back on demand. Mirrors
/// cppcache <c>PersistenceManager</c>
/// (<c>cppcache/include/geode/PersistenceManager.hpp:45</c>).
/// </summary>
/// <remarks>
/// Phase 4 feature (LRU overflow-to-disk; see
/// <see cref="CacheDiskPolicy"/> / <c>LRUOverFlowToDiskAction</c>) — no
/// consumer until then. All members are required (cppcache pure virtual).
/// The opaque persistence handle (cppcache <c>std::shared_ptr&lt;void&gt;</c>)
/// is modelled as <see langword="object"/>?: <see cref="Write"/> returns the
/// handle locating the stored value, which <see cref="Read"/> /
/// <see cref="Destroy"/> take back.
/// </remarks>
public interface IPersistenceManager
{
    /// <summary>
    /// One-time setup after construction. cppcache <c>init</c>
    /// (<c>PersistenceManager.hpp:89</c>).
    /// </summary>
    void Init(IRegion region, IReadOnlyDictionary<string, string>? diskProperties);

    /// <summary>
    /// Persist <paramref name="value"/> for <paramref name="key"/> to disk;
    /// returns the handle locating it (cppcache's in/out
    /// <c>persistenceInfo</c>). cppcache <c>write</c>
    /// (<c>PersistenceManager.hpp:71</c>).
    /// </summary>
    object? Write(object key, object? value, object? persistenceInfo);

    /// <summary>Persist all entries of the region. cppcache <c>writeAll</c> (<c>PersistenceManager.hpp:80</c>).</summary>
    bool WriteAll();

    /// <summary>
    /// Read the value for <paramref name="key"/> back from disk using its
    /// <paramref name="persistenceInfo"/> handle. cppcache <c>read</c>
    /// (<c>PersistenceManager.hpp:99</c>).
    /// </summary>
    object? Read(object key, object? persistenceInfo);

    /// <summary>Read all values for the region. cppcache <c>readAll</c> (<c>PersistenceManager.hpp:107</c>).</summary>
    bool ReadAll();

    /// <summary>
    /// Remove the on-disk entry for <paramref name="key"/>. cppcache
    /// <c>destroy</c> (<c>PersistenceManager.hpp:116</c>).
    /// </summary>
    void Destroy(object key, object? persistenceInfo);

    /// <summary>Close / release the persistence manager. cppcache <c>close</c> (<c>PersistenceManager.hpp:123</c>).</summary>
    void Close();
}
