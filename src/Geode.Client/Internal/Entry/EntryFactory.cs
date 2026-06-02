using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Internal.Entry;

internal class EntryFactory(bool concurrencyChecksEnabled)
{
    static readonly ObjectFactory<EntryFactory> _objectFactory
        = ActivatorUtilities.CreateFactory<EntryFactory>([typeof(bool)]);

    public static EntryFactory Create(IServiceProvider serviceProvider, bool concurrencyChecksEnabled)
        => _objectFactory(serviceProvider, [concurrencyChecksEnabled]);

    public MapEntry NewEntry(object key, object newValue,
        int updateCount, int destroyTracker,
        VersionTag? versionTag, VersionStamp? carriedStamp = null)
    {
        if (!concurrencyChecksEnabled)
        {
            if (updateCount >= 0)
            {
                throw new GfErrTypeException(GfErrType.CacheEntryUpdated);
            }
            if (destroyTracker > 0)
            {
                throw new NotImplementedException("EntryFactory.NewEntry: destroy-tracker race detection pending Phase 2+ tracker subsystem.");
            }
        }

        var entry = CreateEntry(key);
        entry.Value = newValue;
        if (entry is IVersionStamp stamp)
        {
            if (versionTag is not null)
            {
                stamp.Stamp.SetVersions(versionTag);
            }
            else if (carriedStamp is not null)
            {
                stamp.Stamp.SetVersions(carriedStamp);
            }
        }

        return entry;
    }

    protected virtual MapEntry CreateEntry(object key)
    {
        return concurrencyChecksEnabled ?
            new VersionedMapEntry(key) :
            new MapEntry(key);
    }
}
