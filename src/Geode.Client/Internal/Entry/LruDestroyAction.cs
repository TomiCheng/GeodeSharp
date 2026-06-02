using Microsoft.Extensions.Logging;
using System;

namespace Geode.Client.Internal.Entry;

internal sealed class LruDestroyAction(LocalRegion region, ILogger<LruDestroyAction> logger)
    : LruAction
{
    public override bool Destroys => true;
    public override bool Distributes => true;
    /// <inheritdoc />
    public override Action ActionType => Action.Destroy;

    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        var key = entry.Key;
        logger.LogDebug("LruDestroy: evicting entry with key [{Key}]", key);

        if (region.IsDestroyed)
        {
            return true;
        }

        try
        {
            await region.DestroyNoThrowAsync(
                key,
                callbackArgument: null,
                updateCount: -1,
                eventFlags: CacheEventFlags.Eviction,
                versionTag: null,
                ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (GeodeException)
        {
            return false;
        }
    }
}
