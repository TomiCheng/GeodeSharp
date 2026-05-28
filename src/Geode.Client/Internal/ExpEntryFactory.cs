using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal;

/// <summary>
/// <see cref="EntryFactory"/> variant that produces expiry-aware
/// <see cref="MapEntry"/> instances (entry-level TTL / idle). Mirrors
/// cppcache <c>ExpEntryFactory</c>
/// (<c>cppcache/src/ExpMapEntry.hpp:76</c>). Skeleton only — used by
/// <see cref="EntriesMapFactory"/>'s expiry branches (Phase 2+).
/// </summary>
internal sealed class ExpEntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    : EntryFactory(serviceProvider, concurrencyChecksEnabled)
{
    static readonly ObjectFactory<ExpEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<ExpEntryFactory>([typeof(bool)]);

    /// <summary>
    /// DI-aware factory. Pre-baked <see cref="ObjectFactory{T}"/> avoids
    /// per-call ctor resolution; same pattern as <see cref="LRUEntriesMap.Create"/>.
    /// </summary>
    /// <param name="concurrencyChecksEnabled"></param>
    internal new static ExpEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    {
        return _objectFactory(serviceProvider, [concurrencyChecksEnabled]);
    }
}
