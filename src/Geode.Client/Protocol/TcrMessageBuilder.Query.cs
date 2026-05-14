namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// cppcache <c>DEFAULT_QUERY_RESPONSE_TIMEOUT</c> = 15 seconds. The
    /// server uses this to abort runaway OQL on its side. Phase 1.4
    /// hard-codes the cppcache default; Phase 1.5 <c>PoolOptions</c>
    /// will surface a user-tunable knob.
    /// </summary>
    private const int DefaultQueryResponseTimeoutMillis = 15_000;

    /// <summary>
    /// Build a <see cref="MessageType.Query"/> (34) request frame.
    /// Mirrors cppcache <c>TcrMessageQuery</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1684-1709</c>); the "send + reply"
    /// flow lives in <c>RemoteQuery::execute</c>
    /// (<c>cppcache/src/RemoteQuery.cpp:67-120</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.Query"/>=34,
    /// NumParts=2 or 3, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 QueryString  0         raw OQL bytes (cppcache writeRegionPart
    ///                          reused — the OQL lives in m_regionName)
    /// 2 EventId      0         18 raw bytes: [3][i64 tid][3][i64 seq]
    /// 3 (optional)   0         4 raw bytes:  i32 BE response timeout ms
    /// </code>
    /// <para>
    /// Part 1 re-uses the "region name" encoding even though no region
    /// is involved &#x2014; cppcache stuffs the OQL into <c>m_regionName</c>
    /// (<c>TcrMessage.cpp:1691</c> comment: "this is querystri[ng]") and
    /// emits it through <see cref="TcrPartBuilder.RegionName"/>, so the
    /// raw bytes hit the wire identically. We do the same to keep the
    /// builder library symmetric.
    /// </para>
    /// <para>
    /// EventId is caller-supplied for parity with
    /// <see cref="Put"/> / <see cref="ClearRegion"/>: server-side
    /// <c>ClientHealthMonitor</c> de-dupes on
    /// <c>(clientId, threadId, sequenceId)</c>, so every request needs
    /// a fresh id. <see cref="Services.ThinClientRegion"/>'s caller
    /// drives it via <see cref="Internal.EventIdGenerator"/>.
    /// </para>
    /// <para>
    /// <paramref name="messageResponseTimeoutMillis"/> mirrors cppcache
    /// <c>messageResponseTimeout</c>: pass <see langword="null"/> to
    /// omit the part (cppcache <c>&lt; 0</c> branch), any non-negative
    /// value to include it. The Phase 1.4 default tracks cppcache's
    /// 15 s default; Phase 1.5 will route a user-tunable value from
    /// <c>PoolOptions</c>.
    /// </para>
    /// </remarks>
    public TcrMessage Query(
        string queryString,
        long eventThreadId,
        long eventSequenceId,
        int? messageResponseTimeoutMillis = DefaultQueryResponseTimeoutMillis,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryString);

        var parts = new List<TcrPart>(3)
        {
            // Part 1 — Query string. cppcache writeRegionPart of the OQL.
            partBuilder.RegionName(queryString),

            // Part 2 — EventId. 18 raw bytes:
            //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 sequenceId BE]
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),
        };

        // Part 3 — Optional response timeout. cppcache writeMillisecondsPart
        //   = writeIntPart = [part_len=4][isObj=0][int32 BE ms].
        if (messageResponseTimeoutMillis is { } ms)
        {
            parts.Add(partBuilder.Raw(w => w.WriteInt32(ms), sizeHint: 4));
        }

        return new TcrMessage(
            MessageType: MessageType.Query,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
