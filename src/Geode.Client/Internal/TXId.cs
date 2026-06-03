namespace Geode.Client.Internal;

/// <summary>
/// Concrete <see cref="ITransactionId"/> backed by a process-wide monotonic
/// int counter. Mirrors cppcache <c>TXId</c>
/// (<c>cppcache/src/TXId.hpp/cpp</c>); counter wraps to <c>1</c> (skipping
/// <c>0</c> = "no tx" sentinel on the wire) on int32 overflow.
/// </summary>
internal sealed class TXId : ITransactionId
{
    private static int _nextTxId;

    public TXId()
    {
        // CAS loop: wrap to 1 (not 0) on int32 overflow — 0 is the wire-protocol
        // sentinel for "no tx".
        int current, next;
        do
        {
            current = Volatile.Read(ref _nextTxId);
            next = current == int.MaxValue ? 1 : current + 1;
        } while (Interlocked.CompareExchange(ref _nextTxId, next, current) != current);
        Id = next;
    }

    public int Id { get; }
}
