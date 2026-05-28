using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="EntryFactory"/> variant that produces <see cref="MapEntry"/>
/// instances with both LRU chain pointers and expiry tracking. Mirrors
/// cppcache <c>LRUExpEntryFactory</c>
/// (<c>cppcache/src/LRUExpMapEntry.hpp:83</c>). Skeleton only — used by
/// <see cref="EntriesMapFactory"/>'s LRU branch when ttl / idle &gt; 0
/// (Phase 2+).
/// </summary>
internal sealed class LRUExpEntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    : EntryFactory(serviceProvider, concurrencyChecksEnabled)
{
    static readonly ObjectFactory<LRUExpEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<LRUExpEntryFactory>([typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Pre-baked <see cref="ObjectFactory{T}"/> avoids
    /// per-call ctor resolution; same pattern as <see cref="LRUEntriesMap.Create"/>.
    /// </summary>
    internal new static LRUExpEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    {
        return _objectFactory(serviceProvider, [concurrencyChecksEnabled]);
    }
}
