using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal class ExpEntryFactory(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
    : EntryFactory(concurrencyChecksEnabled)
{
    readonly bool _concurrencyChecksEnabled = concurrencyChecksEnabled;
    static readonly ObjectFactory<ExpEntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<ExpEntryFactory>([typeof(bool)]);
    public new static ExpEntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
        => _objectFactory(serviceProvider, [concurrencyChecksEnabled]);

    private readonly ExpiryTaskManager _expiryTaskManager = serviceProvider.GetRequiredService<ExpiryTaskManager>();

    protected override MapEntry CreateEntry(object key)
        => _concurrencyChecksEnabled
            ? new VersionedExpMapEntry(_expiryTaskManager, key)
            : new ExpMapEntry(_expiryTaskManager, key);
}
