using Microsoft.Extensions.DependencyInjection;

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
    /// Wire layout ??Header (<see cref="MessageType.Request"/>=0,
    /// NumParts=2 or 3, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key          1         DSCode-tagged serialized key
    /// 3 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Compared with <see cref="Put"/> the layout is much simpler ??no
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
    /// <summary>Sync wrapper for tests; production code uses <see cref="GetAsync"/>.</summary>
    public TcrMessage Get(string regionName, object key, object? callbackArgument = null, int transactionId = MetaTransactionId) =>
        GetAsync(regionName, key, callbackArgument, transactionId).GetAwaiter().GetResult();

    public async ValueTask<TcrMessage> GetAsync(
        string regionName,
        object key,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        var parts = new List<TcrPart>(3)
        {
            partBuilder.RegionName(regionName),
            await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, key, ct: ct)),
        };

        if (callbackArgument is not null)
        {
            parts.Add(await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, callbackArgument, ct: ct)));
        }

        return ActivatorUtilities.CreateInstance<TcrMessage>(_serviceProvider, MessageType.Request, transactionId, (byte)0, parts);
    }
}
