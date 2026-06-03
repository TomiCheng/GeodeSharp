namespace Geode.Client.Internal;

/// <summary>
/// Thread-pinned client transaction state. Mirrors cppcache
/// <c>TXState</c> (<c>cppcache/src/TXState.hpp</c>). Skeleton only —
/// members land with the transaction phase (Phase 4+).
/// </summary>
internal sealed class TXState
{
    public TXId TransactionId { get; } = new();

    public bool IsDirty { get; private set; }

    public void SetDirty() => IsDirty = true;
    public ThinClientBaseDM? DM { get; set; }
    public bool IsPrepared { get; private set; }
}
