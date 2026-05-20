using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    // EventId per-i64 type code. cppcache EventId::writeIdsData
    // (cppcache/src/EventId.hpp line 95) always emits 3 = "long".
    private const byte EventIdLongCode = 3;

    /// <summary>
    /// Build a <see cref="MessageType.Put"/> (7) request frame. Mirrors
    /// cppcache <c>TcrMessagePut</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1989-2034</c>); the "send + reply"
    /// flow lives in <c>ThinClientRegion::putNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:888-947</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout ??Header (<see cref="MessageType.Put"/>=7,
    /// NumParts=7 or 8, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Operation    1         DSCode.NullObj (operation placeholder)
    /// 3 Flags        0         i32 = 0
    /// 4 Key          1         DSCode-tagged serialized key
    /// 5 isDelta      1         DSCode.CacheableBoolean + 1 byte
    /// 6 Value        1         DSCode-tagged serialized value
    /// 7 EventId      0         18 raw bytes: [3][i64 tid][3][i64 seq]
    /// 8 (optional)   1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// Key, value, and callback all flow through
    /// <see cref="Serialization.SerializationRegistry"/>: a type without
    /// a registered <c>IDataConverter</c> surfaces as
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// Phase 1.2 ships <c>Int32DataConverter</c> + <c>BooleanDataConverter</c>;
    /// the built-in set widens as more codecs land.
    /// </para>
    /// <para>
    /// <b>Value vs cppcache's CacheableBytes shortcut.</b> cppcache's
    /// <c>writeObjectPart</c> has a special-case for
    /// <c>CacheableBytes</c>: it skips the DSCode and writes raw bytes
    /// with <c>IsObject=0</c>. We don't take that shortcut here ??the
    /// registry path always emits DSCode-tagged objects with
    /// <c>IsObject=1</c>. The shortcut is a wire optimisation, not a
    /// correctness requirement; the server reads either form. We'll
    /// reinstate it when <c>BytesDataConverter</c> lands and we want to
    /// match cppcache's exact byte count.
    /// </para>
    /// <para>
    /// <b>EventId is caller-supplied.</b> cppcache generates it inline
    /// inside <c>writeEventIdPart</c> from <c>EventIdTSS</c>; we keep
    /// the values as parameters so <see cref="Internal.ThinClientRegion"/>
    /// can drive them from <see cref="Services.EventIdGenerator"/> (DI
    /// Scoped) and unit tests can pin deterministic ids.
    /// </para>
    /// </remarks>
    public TcrMessage Put(string regionName, object key, object value, object? callbackArgument, long eventThreadId, long eventSequenceId, int transactionId = MetaTransactionId, bool isDelta = false) =>
        PutAsync(regionName, key, value, callbackArgument, eventThreadId, eventSequenceId, transactionId, isDelta).GetAwaiter().GetResult();

    public async ValueTask<TcrMessage> PutAsync(
        string regionName,
        object key,
        object value,
        object? callbackArgument,
        long eventThreadId,
        long eventSequenceId,
        int transactionId = MetaTransactionId,
        bool isDelta = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var parts = new List<TcrPart>(8)
        {
            partBuilder.RegionName(regionName),
            partBuilder.NullObj(),
            partBuilder.Int32(0),
            await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, key, ct: ct)),
            partBuilder.CacheableBoolean(isDelta),
            await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, value, ct: ct)),
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
        };

        if (callbackArgument is not null)
        {
            parts.Add(await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, callbackArgument, ct: ct)));
        }

        return ActivatorUtilities.CreateInstance<TcrMessage>(_serviceProvider, MessageType.Put, transactionId, (byte)0, parts);
    }
}
