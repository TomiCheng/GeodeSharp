using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal.Entry;


internal sealed class LruLocalDestroyAction(
    LocalRegion region,
    ILogger<LruLocalDestroyAction> logger) : LruAction
{
    public override bool Destroys => true;
    /// <inheritdoc />
    public override Action ActionType => Action.LocalDestroy;

    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        var key = entry.Key;
        logger.LogDebug("LruLocalDestroy: evicting entry with key [{Key}]", key);

        try
        {
            await region.DestroyNoThrowAsync(
                key,
                callbackArgument: null,
                updateCount: -1,
                eventFlags: CacheEventFlags.Eviction | CacheEventFlags.Local,
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
