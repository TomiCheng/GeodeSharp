using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal class LruExpEntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    : EntryFactory(concurrencyChecksEnabled)
{
    readonly bool _concurrencyChecksEnabled = concurrencyChecksEnabled;
    static readonly ObjectFactory<LruExpEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<LruExpEntryFactory>([typeof(bool)]);
    public new static LruExpEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
        => _objectFactory(serviceProvider, [concurrencyChecksEnabled]);

    private readonly ExpiryTaskManager _expiryTaskManager = serviceProvider.GetRequiredService<ExpiryTaskManager>();

    protected override MapEntry CreateEntry(object key)
        => _concurrencyChecksEnabled
            ? new VersionedLruExpMapEntry(_expiryTaskManager, key)
            : new LruExpMapEntry(_expiryTaskManager, key);
}
