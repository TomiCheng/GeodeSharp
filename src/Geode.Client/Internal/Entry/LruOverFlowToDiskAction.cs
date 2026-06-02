using Geode.Client.Services;
using Microsoft.Extensions.Logging;
using System;

namespace Geode.Client.Internal.Entry;

internal sealed class LruOverFlowToDiskAction(
    ILogger<LruOverFlowToDiskAction> logger,
    SerializationRegistry serializationRegistry,
    LocalRegion region,
    LruEntriesMap entriesMap)
    : LruAction
{
    public override Action ActionType => Action.OverflowToDisk;

    public override bool Overflows => true;

    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        if (region.IsDestroyed)
        {
            logger.LogError(
                "[internal error] :: OverflowAction: region is being destroyed, so not evicting entries");
            return false;
        }

        var key = entry.Key;
        var value = entry.Value;
        if (value is null)
        {
            logger.LogError("[internal error]:: OverflowAction: destroyed entry added to LRU list");
            throw new FatalInternalException("OverflowAction: destroyed entry added to LRU list");
        }

        var lruProps = ((ILruEntryProperties)entry).LruProperties;
        var persistenceInfo = lruProps.PersistenceInfo;
        var setInfo = false;
        if (persistenceInfo is null)
        {
            setInfo = true;
        }
        var pm = region.PersistenceManager;
        try
        {
            persistenceInfo = await pm!.WriteAsync(key, value, persistenceInfo, ct).ConfigureAwait(false);
        }
        catch (DiskFailureException ex)
        {
            logger.LogError(ex, "DiskFailureException");
            return false;
        }
        catch (GeodeException ex)
        {
            logger.LogError(ex, "write to persistence layer failed");
            return false;
        }
        if (setInfo == true)
        {
            lruProps.PersistenceInfo = persistenceInfo;
        }

        region.RegionStats.Overflow();
        region.CachePerfStats.Overflow();

        entry.Value = CacheableToken.Overflowed;
        if (entriesMap is not null)
        {
            var newSize = CacheableToken.Overflowed.ObjectSize - serializationRegistry.CheckAndGetObjectSize(value);
            entriesMap.UpdateMapSize(newSize);
        }
        return true;
    }
}
