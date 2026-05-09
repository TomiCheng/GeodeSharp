using System.Text;

namespace Geode.Client.Protocol;

/// <summary>
/// Static factory for the TCR request frames (<see cref="TcrMessage"/>)
/// each Geode operation puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One method per <see cref="MessageType"/>. The collection mirrors
/// cppcache's <c>TcrMessage.hpp</c> family of <c>TcrMessage*</c>
/// subclasses (<c>TcrMessagePing</c>, <c>TcrMessagePut</c>,
/// <c>TcrMessageRequest</c>, ...) — same per-operation recipe, expressed
/// as functions returning an immutable <see cref="TcrMessage"/> rather
/// than as a class hierarchy.
/// </para>
/// <para>
/// Pure functions: no I/O, no hidden state. The "send it + handle the
/// reply" half lives separately on <see cref="TcrConnection"/>
/// extensions (<see cref="Operations.PingExtensions"/> etc.); callers
/// can also compose <see cref="TcrConnection.SendRequestAsync"/> with a
/// builder result directly when they want full control over reply
/// dispatch.
/// </para>
/// <para>
/// All MessageTypes use <see cref="MetaTransactionId"/> = -1 unless they
/// participate in a Geode transaction (Phase 11+). Mirrors cppcache
/// <c>TcrMessage::writeHeader</c>: <c>m_txId = -1</c> when no
/// <c>TxState</c> is present.
/// </para>
/// </remarks>
internal sealed class TcrMessageBuilder(TcrPartBuilder partBuilder)
{
    /// <summary>
    /// Sentinel used for any request that isn't part of a Geode
    /// transaction. Geode transactions land in Phase 11+.
    /// </summary>
    public const int MetaTransactionId = -1;

    // EventId per-i64 type code. cppcache EventId::writeIdsData
    // (cppcache/src/EventId.hpp line 95) always emits 3 = "long".
    private const byte EventIdLongCode = 3;

    /// <summary>
    /// Build a <see cref="MessageType.Ping"/> request frame.
    /// Mirrors cppcache <c>TcrMessagePing</c>.
    /// </summary>
    public TcrMessage Ping() =>
        new(
            MessageType: MessageType.Ping,
            TransactionId: MetaTransactionId,
            EarlyAck: 0,
            Parts: []);

    /// <summary>
    /// Build a <see cref="MessageType.Put"/> request frame. Mirrors
    /// cppcache <c>TcrMessagePut</c> (<c>cppcache/src/TcrMessage.cpp:1989</c>)
    /// at the wire level — the "send + reply" flow lives in
    /// <c>ThinClientRegion::putNoThrow_remote</c>.
    /// </summary>
    /// <remarks>
    /// Phase 3 only handles <c>string</c> keys and <c>byte[]</c> values
    /// (the walking-skeleton subset). Phase 4 expands to int / long /
    /// bool / Date via a serialization registry; this signature is stable.
    /// </remarks>
    public TcrMessage Put(
        string regionName,
        object key,
        object? value,
        object? callbackArgument,
        long eventThreadId,
        long eventSequenceId,
        int transactionId = MetaTransactionId,
        bool isDelta = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        // Phase 3 type guards. Phase 4 replaces with serialization registry.
        if (key is not string keyString)
        {
            throw new NotSupportedException(
                $"Phase 3 only supports string keys; got {key.GetType()}.");
        }
        if (value is null)
        {
            throw new NotSupportedException(
                "Phase 3 does not support null value (Geode treats it as " +
                "invalidate, not put). Use a future Destroy / Invalidate op.");
        }
        if (value is not byte[] valueBytes)
        {
            throw new NotSupportedException(
                $"Phase 3 only supports byte[] values; got {value.GetType()}.");
        }
        if (valueBytes.Length == 0)
        {
            throw new NotSupportedException(
                "Phase 3 does not support empty byte[] values; the empty " +
                "CacheableBytes IsObject=2 path needs a serializer to emit it.");
        }

        var parts = new List<TcrPart>(8)
        {
            // Part 1 — Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RawBytes(Encoding.ASCII.GetBytes(regionName)),

            // Part 2 — Operation = NullObj.
            partBuilder.NullObj(),

            // Part 3 — Flags i32 = 0 (cppcache writeIntPart(0)).
            partBuilder.Int32(0),

            // Part 4 — Key (DSCode-tagged string).
            partBuilder.Object(w => w.WriteString(keyString)),

            // Part 5 — isDelta as CacheableBoolean.
            partBuilder.CacheableBoolean(isDelta),

            // Part 6 — Value. CacheableBytes shortcut: raw bytes, IsObject=0
            // (cppcache writeObjectPart line 676).
            partBuilder.RawBytes(valueBytes),

            // Part 7 — EventId. 18 raw bytes:
            //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 sequenceId BE]
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
        };

        // Part 8 — Optional callback argument.
        if (callbackArgument is not null)
        {
            if (callbackArgument is not string cbString)
            {
                throw new NotSupportedException(
                    $"Phase 3 only supports null or string callback argument; " +
                    $"got {callbackArgument.GetType()}.");
            }
            parts.Add(partBuilder.Object(w => w.WriteString(cbString)));
        }

        return new TcrMessage(
            MessageType: MessageType.Put,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
