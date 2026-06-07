using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal.Entry;


internal sealed class LruLocalInvalidateAction(
    LocalRegion region,
    ILogger<LruLocalInvalidateAction> logger) : LruAction
{
    public override bool Invalidates => true;
    /// <inheritdoc />
    public override Action ActionType => Action.LocalInvalidate;

    public override async Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
    {
        var key = entry.Key;
        logger.LogDebug("LruLocalInvalidate: evicting entry with key [{Key}]", key);

        // cppcache LRULocalInvalidateAction::evict (LRUAction.cpp:117): a destroyed
        // region leaves err == GF_NOERR → success. EVICTION | LOCAL flags fire the
        // listeners without a wire round-trip.
        if (region.IsDestroyed)
        {
            return true;
        }

        try
        {
            await region.InvalidateNoThrowAsync(
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
