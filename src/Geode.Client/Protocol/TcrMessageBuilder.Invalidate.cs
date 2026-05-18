namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Invalidate"/> (83) request frame.
    /// Mirrors cppcache <c>TcrMessageInvalidate</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1896-1932</c>); the "send + reply"
    /// flow lives in <c>ThinClientRegion::invalidateNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:852-886</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.Invalidate"/>=83,
    /// NumParts=3 or 4, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key          1         DSCode-tagged serialized key
    /// 3 EventId      0         18 raw bytes: [3][i64 tid][3][i64 seq]
    /// 4 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Smaller than <see cref="Destroy"/> (no expectedOldValue /
    /// operation slots) because cppcache <c>TcrMessageInvalidate</c>
    /// has a single semantic — there is no conditional / overload
    /// counterpart sharing the ctor.
    /// </para>
    /// <para>
    /// Key and callback flow through
    /// <see cref="Serialization.SerializationRegistry"/>: a type without
    /// a registered <c>IDataConverter</c> surfaces as
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// </para>
    /// <para>
    /// EventId is caller-supplied for the same reason as <see cref="Put"/> /
    /// <see cref="Destroy"/> &#x2014; <see cref="Internal.ThinClientRegion"/>
    /// drives it from <see cref="Services.EventIdGenerator"/>.
    /// </para>
    /// </remarks>
    public TcrMessage Invalidate(
        string regionName,
        object key,
        long eventThreadId,
        long eventSequenceId,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        var parts = new List<TcrPart>(4)
        {
            // Part 1 — Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — Key (DSCode-tagged via registry).
            partBuilder.Object(w => _serializationRegistry.WriteObject(w, key)),

            // Part 3 — EventId. 18 raw bytes:
            //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 sequenceId BE]
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
        };

        // Part 4 — Optional callback argument (DSCode-tagged via registry).
        if (callbackArgument is not null)
        {
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, callbackArgument)));
        }

        return new TcrMessage(
            MessageType: MessageType.Invalidate,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
