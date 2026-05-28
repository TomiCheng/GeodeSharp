using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// Builds <see cref="MapEntry"/> instances for an <see cref="EntriesMap"/>.
/// Base of the 4-way factory hierarchy (plain / Exp / LRU / LRU+Exp);
/// the base itself produces a plain entry. Mirrors cppcache
/// <c>EntryFactory</c> (<c>cppcache/src/MapEntryImpl.hpp:127</c>).
/// Skeleton only — <c>newMapEntry</c> body lands with the caching-enabled
/// phase (Phase 2+).
/// </summary>
internal class EntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
{
    IServiceProvider _ = serviceProvider;

    /// <summary>
    /// cppcache <c>m_concurrencyChecksEnabled</c>
    /// (<c>cppcache/src/MapEntryImpl.hpp:139</c>): whether per-entry
    /// version-tag tracking is enabled. Forwarded down to <c>newMapEntry</c>
    /// when subclass bodies land (Phase 2+).
    /// </summary>
    protected readonly bool ConcurrencyChecksEnabled = concurrencyChecksEnabled;

    static readonly ObjectFactory<EntryFactory> _objectFactory
    = ActivatorUtilities.CreateFactory<EntryFactory>([typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Pre-baked <see cref="ObjectFactory{T}"/> avoids
    /// per-call ctor resolution; same pattern as <see cref="LRUEntriesMap.Create"/>.
    /// </summary>
    /// <param name="nullconcurrencyChecksEnabled"></param>
    internal static EntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    {
        return _objectFactory(serviceProvider, [concurrencyChecksEnabled]);
    }
}
