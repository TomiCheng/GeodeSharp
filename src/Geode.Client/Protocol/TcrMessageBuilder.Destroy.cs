using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Destroy"/> (9) request frame.
    /// Mirrors cppcache <c>TcrMessageDestroy</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1934-1986</c>) ??specifically
    /// the <c>value == nullptr &amp;&amp; isUserNullValue == false</c>
    /// branch, which is what <c>destroyNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:959-999</c>) calls. The
    /// other branch (caller-supplied <c>expectedOldValue</c>) is
    /// reserved for the conditional <c>RemoveEx</c> overload, deferred
    /// to a later phase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout ??Header (<see cref="MessageType.Destroy"/>=9,
    /// NumParts=5 or 6, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part              IsObject  Payload
    /// 1 Region            0         raw region path bytes (ASCII; no DSCode)
    /// 2 Key               1         DSCode-tagged serialized key
    /// 3 ExpectedOldValue  1         DSCode.NullObj (the value=null branch)
    /// 4 Operation         1         DSCode.NullObj (operation slot)
    /// 5 EventId           0         18 raw bytes: [3][i64 tid][3][i64 seq]
    /// 6 (optional)        1         DSCode-tagged callback argument
    /// </code>
    /// <para>
    /// <b>Why two NullObj parts in the middle.</b> cppcache uses one
    /// <c>TcrMessageDestroy</c> ctor to serve two distinct public APIs:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>destroyNoThrow_remote</c> (unconditional destroy) ??    ///         passes <c>value=nullptr, isUserNullValue=false</c> ??    ///         emits the layout above with both expectedOldValue and
    ///         operation set to NullObj. cppcache <c>Destroy65.java</c>
    ///         interprets that pair as "plain destroy".</item>
    ///   <item><c>removeNoThrow_remote</c> (conditional remove ??    ///         <c>Region::remove(key, value)</c>) ??passes a real
    ///         <c>value</c> + <c>removeByte=8</c> in the operation slot.
    ///         Same wire shape, different semantics. Not built yet.</item>
    /// </list>
    /// <para>
    /// Key and callback both flow through
    /// <see cref="Serialization.SerializationRegistry"/>: a type without
    /// a registered <c>IDataConverter</c> surfaces as
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// </para>
    /// <para>
    /// EventId is caller-supplied for the same reason as
    /// <see cref="Put"/> ??<see cref="Internal.ThinClientRegion"/>
    /// drives it from <see cref="Services.EventIdGenerator"/>.
    /// </para>
    /// </remarks>
    public TcrMessage Destroy(string regionName, object key, long eventThreadId, long eventSequenceId, object? callbackArgument = null, int transactionId = MetaTransactionId) =>
        DestroyAsync(regionName, key, eventThreadId, eventSequenceId, callbackArgument, transactionId).GetAwaiter().GetResult();

    public async ValueTask<TcrMessage> DestroyAsync(
        string regionName,
        object key,
        long eventThreadId,
        long eventSequenceId,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(key);

        var parts = new List<TcrPart>(6)
        {
            partBuilder.RegionName(regionName),
            await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, key, ct: ct)),
            partBuilder.NullObj(),
            partBuilder.NullObj(),
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

        return ActivatorUtilities.CreateInstance<TcrMessage>(_serviceProvider, MessageType.Destroy, transactionId, (byte)0, parts);
    }
}
