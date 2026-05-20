using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.RemoveAll"/> (109) request frame.
    /// Mirrors cppcache <c>TcrMessageRemoveAll</c>
    /// (<c>cppcache/src/TcrMessage.cpp:2424-2468</c>); the "send +
    /// chunked reply" flow lives in
    /// <c>ThinClientRegion::multiHopRemoveAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1810-1863</c>) and is
    /// wired up later in Phase 1.3.b once the chunked-reply
    /// infrastructure lands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout &#x2014; Header (<see cref="MessageType.RemoveAll"/>=109,
    /// NumParts=5+<c>keys.Count</c>, TransactionId=-1, EarlyAck=0)
    /// followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 EventId      0         18 raw bytes: [3][i64 tid][3][i64 baseSeq]
    /// 3 Flags        0         i32 BE; bit0=EMPTY, bit1=ConcurrencyChecks
    /// 4 Callback     1         DSCode-tagged callback, or DSCode.NullObj
    /// 5 KeyCount     0         i32 BE = keys.Count
    /// 6..5+N Key     1         DSCode-tagged serialized key (each)
    /// </code>
    /// <para>
    /// <b>Part order differs from <see cref="Destroy"/> /
    /// <see cref="Invalidate"/>.</b> cppcache puts EventId immediately
    /// after the region name (no ExpectedOldValue / Operation slots),
    /// then flags / callback / keyCount / N keys. The callback part is
    /// <b>always emitted</b> &#x2014; cppcache <c>writeObjectPart(nullptr)</c>
    /// writes <see cref="DSCode.NullObj"/> rather than skipping the
    /// part &#x2014; so the part count is unconditionally
    /// <c>5 + keys.Count</c> (no optional-callback branch like Destroy
    /// / Invalidate).
    /// </para>
    /// <para>
    /// <b>Flags semantics</b> (cppcache <c>TcrMessage.cpp:2446-2459</c>):
    /// bit 0 (<c>kFlagEmpty=0x01</c>) is set when the region has
    /// <c>caching-enabled=false</c>; bit 1
    /// (<c>kFlagConcurrencyChecks=0x02</c>) when concurrency checks
    /// are enabled. The server uses these to decide whether to ship
    /// versionTags back in the chunked reply. Phase 1.3 MVP regions
    /// don't yet expose either attribute &#x2014; caller passes
    /// <c>0</c>; revisit when client-side caching lands (Phase 4+).
    /// </para>
    /// <para>
    /// <b>EventId reservation.</b> cppcache calls
    /// <c>writeEventIdPart(keys.size() - 1)</c> &#x2014; only one
    /// <c>(threadId, baseSeq)</c> pair goes on the wire, but the
    /// per-thread sequence counter is bumped by <c>N-1</c> extra slots
    /// so the server can dedup each key's logical event as
    /// <c>(clientId, threadId, baseSeq+i)</c> for
    /// <c>i &#x2208; [0, N)</c>. Our
    /// <see cref="Services.EventIdGenerator"/> uses a single shared
    /// <c>Interlocked</c> counter; the caller must allocate <c>N</c>
    /// consecutive sequence ids upfront and pass the lowest
    /// (<c>baseSeq</c>) here. Plumbing is the caller's responsibility
    /// (the builder has no view into the generator) and lands with
    /// the <see cref="Internal.ThinClientRegion"/> wiring later in
    /// Phase 1.3.b.
    /// </para>
    /// <para>
    /// Each key flows through
    /// <see cref="Serialization.SerializationRegistry"/>; a type
    /// without a registered <c>IDataConverter</c> surfaces as
    /// <see cref="NotSupportedException"/> from inside the registry.
    /// </para>
    /// <para>
    /// The optional <c>messageResponseTimeout</c> part cppcache
    /// appends when <c>m_messageResponseTimeout &#x2265; 0</c>
    /// (<c>TcrMessage.cpp:2439-2441</c>) is <b>not</b> emitted
    /// &#x2014; cppcache initialises that member to <c>-1</c> and only
    /// newer timeout-aware overloads bump it. Mirrors our same
    /// decision for <see cref="ClearRegion"/>; revisit if a real
    /// timeout API is ever added.
    /// </para>
    /// </remarks>
    /// <param name="regionName">Full region path (e.g. <c>"/orders"</c>).</param>
    /// <param name="keys">Keys to remove. Empty is rejected &#x2014;
    /// cppcache's <c>keys.size() - 1</c> reserve underflows on zero
    /// and the round-trip would be a no-op anyway.</param>
    /// <param name="eventThreadId">Thread component of the EventId pair.</param>
    /// <param name="eventSequenceId">Base sequence id; see "EventId
    /// reservation" in remarks.</param>
    /// <param name="callbackArgument">Forwarded to server-side
    /// listeners / writers; <c>null</c> ships
    /// <see cref="DSCode.NullObj"/>.</param>
    /// <param name="transactionId">Geode txn id;
    /// <see cref="MetaTransactionId"/> for non-transactional ops.</param>
    public TcrMessage RemoveAll(string regionName, IReadOnlyCollection<object> keys, long eventThreadId, long eventSequenceId, object? callbackArgument = null, int transactionId = MetaTransactionId) =>
        RemoveAllAsync(regionName, keys, eventThreadId, eventSequenceId, callbackArgument, transactionId).GetAwaiter().GetResult();

    public async ValueTask<TcrMessage> RemoveAllAsync(
        string regionName,
        IReadOnlyCollection<object> keys,
        long eventThreadId,
        long eventSequenceId,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException(
                "RemoveAll requires at least one key.", nameof(keys));
        }

        var parts = new List<TcrPart>(5 + keys.Count)
        {
            partBuilder.RegionName(regionName),
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
            partBuilder.Int32(0),
            callbackArgument is null
                ? partBuilder.NullObj()
                : await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, callbackArgument, ct: ct)),
            partBuilder.Int32(keys.Count),
        };

        foreach (var key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            parts.Add(await partBuilder.ObjectAsync(async w => await _serializationRegistry.WriteObjectAsync(w, key, ct: ct)));
        }

        return ActivatorUtilities.CreateInstance<TcrMessage>(_serviceProvider, MessageType.RemoveAll, transactionId, (byte)0, parts);
    }
}
