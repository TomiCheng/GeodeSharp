namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Request"/> (Get) request frame.
    /// Mirrors cppcache <c>TcrMessageRequest</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1858</c>); the "send + reply" flow
    /// lives in <c>ThinClientRegion::getNoThrow_remote</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header
    /// (<see cref="MessageType.Request"/>=0, NumParts=2 or 3,
    /// TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part        IsObject  Payload
    /// 1 Region      0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key         1         DSCode-tagged serialized key
    /// 3 (optional)  1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Compared with <see cref="Put"/> this is much simpler — no
    /// Operation / Flags / isDelta / Value / EventId parts.
    /// </para>
    /// <para>
    /// Phase 3 only handles <c>string</c> keys and <c>string</c> callback
    /// arguments. Phase 4 expands via the serialization registry; this
    /// signature is stable.
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

        // Phase 3 type guards. Phase 4 replaces with serialization registry.
        if (key is not string keyString)
        {
            throw new NotSupportedException(
                $"Phase 3 only supports string keys; got {key.GetType()}.");
        }

        var parts = new List<TcrPart>(3)
        {
            // Part 1 — Region name. Raw ASCII bytes (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — Key (DSCode-tagged string).
            partBuilder.Object(w => w.WriteString(keyString)),
        };

        // Part 3 — Optional callback argument.
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
            MessageType: MessageType.Request,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
