using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="EntryFactory"/> variant that produces LRU-tracking
/// <see cref="MapEntry"/> instances (per-entry LRU chain pointers).
/// Mirrors cppcache <c>LRUEntryFactory</c>
/// (<c>cppcache/src/LRUMapEntry.hpp:105</c>). Skeleton only — used by
/// <see cref="EntriesMapFactory"/>'s LRU branch when ttl / idle are
/// both zero (Phase 2+).
/// </summary>
internal sealed class LRUEntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    : EntryFactory(serviceProvider, concurrencyChecksEnabled)
{
    static readonly ObjectFactory<LRUEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<LRUEntryFactory>([typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Pre-baked <see cref="ObjectFactory{T}"/> avoids
    /// per-call ctor resolution; same pattern as <see cref="LRUEntriesMap.Create"/>.
    /// </summary>
    /// <param name="nullconcurrencyChecksEnabled"></param>
    internal new static LRUEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    {
        return _objectFactory(serviceProvider, [concurrencyChecksEnabled]);
    }
}
