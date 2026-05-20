using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.ClearRegion"/> (36) request frame.
    /// Mirrors cppcache <c>TcrMessageClearRegion</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1644-1682</c>); the "send + reply"
    /// flow lives in <c>ThinClientRegion::clear</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:767-808</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout ??Header (<see cref="MessageType.ClearRegion"/>=36,
    /// NumParts=2 or 3, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 EventId      0         18 raw bytes: [3][i64 tid][3][i64 seq]
    /// 3 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// <b>No key part</b> ??clear is region-wide.
    /// <b>No millisecondsResponseTimeout part</b> ??cppcache writes it
    /// only when <c>messageResponseTimeout &gt;= 0</c>, but
    /// <c>ThinClientRegion::clear</c> hard-codes <c>std::chrono::milliseconds(-1)</c>
    /// when invoking the ctor (cppcache/src/ThinClientRegion.cpp:777),
    /// so the part is never emitted on the standard path.
    /// </para>
    /// <para>
    /// EventId is caller-supplied for the same reason as <see cref="Put"/> /
    /// <see cref="Destroy"/> / <see cref="Invalidate"/> &#x2014;
    /// <see cref="Internal.ThinClientRegion"/> drives it from
    /// <see cref="Services.EventIdGenerator"/>. Server-side
    /// <c>ClientHealthMonitor</c> de-dupes on
    /// <c>(clientId, threadId, sequenceId)</c>, so a fresh id is required
    /// even though clear has no per-key payload.
    /// </para>
    /// </remarks>
    public TcrMessage ClearRegion(
        string regionName,
        long eventThreadId,
        long eventSequenceId,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);

        var parts = new List<TcrPart>(3)
        {
            // Part 1 ??Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 ??EventId. 18 raw bytes:
            //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 sequenceId BE]
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
        };

        // Part 3 ??Optional callback argument (DSCode-tagged via registry).
        if (callbackArgument is not null)
        {
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, callbackArgument)));
        }

        return ActivatorUtilities.CreateInstance<TcrMessage>(
            _serviceProvider,
            MessageType.ClearRegion,
            transactionId,
            (byte)0,
            parts);
    }
}
