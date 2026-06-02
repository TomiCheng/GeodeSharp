using System;

namespace Geode.Client.Internal.Entry;


internal sealed class LruLocalInvalidateAction(LocalRegion region) : LruAction
{
    public override bool Invalidates => true;
    /// <inheritdoc />
    public override Action ActionType => Action.LocalInvalidate;

    public override Task<bool> EvictAsync(MapEntry entry, CancellationToken ct = default)
        => throw new NotImplementedException(
            "LRULocalInvalidateAction.EvictAsync: pending region local-invalidate path.");
}
