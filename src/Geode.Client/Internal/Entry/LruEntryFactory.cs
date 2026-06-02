using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal class LruEntryFactory(bool concurrencyChecksEnabled)
    : EntryFactory(concurrencyChecksEnabled)
{
    readonly bool _concurrencyChecksEnabled = concurrencyChecksEnabled;

    static readonly ObjectFactory<LruEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<LruEntryFactory>([typeof(bool)]);

    public new static LruEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
        => _objectFactory(serviceProvider, [concurrencyChecksEnabled]);

    protected override MapEntry CreateEntry(object key)
        => _concurrencyChecksEnabled
            ? new VersionedLruMapEntry(key)
            : new LruMapEntry(key);
}
