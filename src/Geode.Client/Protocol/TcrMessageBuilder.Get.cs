namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Request"/> (0, "Get") request
    /// frame. Mirrors cppcache <c>TcrMessageRequest</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1858-1898</c>); the "send + reply"
    /// flow lives in <c>ThinClientRegion::getNoThrow_remote</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.Request"/>=0,
    /// NumParts=2 or 3, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key          1         DSCode-tagged serialized key
    /// 3 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Compared with <see cref="Put"/> the layout is much simpler — no
    /// Operation / Flags / isDelta / Value / EventId parts. Get doesn't
    /// produce a server-visible event, so there's nothing to dedup.
    /// </para>
    /// <para>
    /// Key and callback both flow through
    /// <see cref="Serialization.SerializationRegistry"/>: a type without
    /// a registered <c>IDataConverter</c> surfaces as
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// Phase 1.2 ships <c>Int32DataConverter</c> + <c>BooleanDataConverter</c>;
    /// the built-in set widens as more codecs land.
    /// </para>
    /// </remarks>
    public TcrMessage Get(
        string regionName,
        object key,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        var parts = new List<TcrPart>(3)
        {
            // Part 1 — Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — Key (DSCode-tagged via registry).
            partBuilder.Object(w => _serializationRegistry.WriteObject(w, key)),
        };

        // Part 3 — Optional callback argument (DSCode-tagged via registry).
        if (callbackArgument is not null)
        {
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, callbackArgument)));
        }

        return new TcrMessage(
            MessageType: MessageType.Request,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
