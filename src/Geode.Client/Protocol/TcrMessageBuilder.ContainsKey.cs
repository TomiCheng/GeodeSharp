namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.ContainsKey"/> (38) request frame.
    /// Mirrors cppcache <c>TcrMessageContainsKey</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1808-1843</c>); the "send +
    /// reply" flow lives in
    /// <c>ThinClientRegion::containsKeyOnServer</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:676-720</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.ContainsKey"/>=38,
    /// NumParts=3 or 4, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key          1         DSCode-tagged serialized key
    /// 3 Op-flag      0         int32 = 0 (containsKey) / 1 (containsValueForKey)
    /// 4 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Op-flag is cppcache's mechanism for letting one wire type
    /// (<c>CONTAINS_KEY</c>) serve both <c>containsKeyOnServer</c> and
    /// <c>containsValueForKey</c>. Phase 1.2 only exercises the
    /// <c>containsKey</c> branch (<paramref name="isContainsKey"/> =
    /// <c>true</c>); the <c>containsValueForKey</c> variant ships when
    /// that public API surfaces.
    /// </para>
    /// <para>
    /// Key and callback-argument encoding goes through
    /// <see cref="Serialization.SerializationRegistry"/>: types
    /// without a registered <c>IDataConverter</c> throw
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// Phase 1.2 ships <c>Int32DataConverter</c> only; the built-in
    /// set widens in Phase 1.2.c.
    /// </para>
    /// </remarks>
    public TcrMessage ContainsKey(
        string regionName,
        object key,
        object? callbackArgument = null,
        bool isContainsKey = true,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        var parts = new List<TcrPart>(4)
        {
            // Part 1 — Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — Key (DSCode-tagged). Registry writes DSCode byte
            // + payload via the converter for key's runtime type.
            partBuilder.Object(w => _serializationRegistry.WriteObject(w, key)),

            // Part 3 — Op-flag i32 (0 = containsKey, 1 = containsValueForKey).
            // cppcache writeIntPart(isContainsKey ? 0 : 1).
            partBuilder.Int32(isContainsKey ? 0 : 1),
        };

        // Part 4 — Optional callback argument. Same registry path —
        // any type with a registered converter works; otherwise the
        // registry throws NotSupportedException.
        if (callbackArgument is not null)
        {
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, callbackArgument)));
        }

        return new TcrMessage(
            MessageType: MessageType.ContainsKey,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
