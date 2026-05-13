namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.PutAll"/> (56) request frame.
    /// Mirrors cppcache <c>TcrMessagePutAll</c>
    /// (<c>cppcache/src/TcrMessage.cpp:2354-2422</c>); the "send +
    /// chunked reply" flow lives in
    /// <c>ThinClientRegion::multiHopPutAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1476-1540</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout &#x2014; Header (<see cref="MessageType.PutAll"/>=56,
    /// NumParts=5+<c>map.Count</c>*2, TransactionId=-1, EarlyAck=0)
    /// followed by:
    /// </para>
    /// <code>
    /// # Part            IsObject  Payload
    /// 1 Region          0         raw region path bytes (ASCII; no DSCode)
    /// 2 EventId         0         18 raw bytes: [3][i64 tid][3][i64 baseSeq]
    /// 3 SkipCallbacks   0         i32 BE = 0 (cppcache placeholder; always 0)
    /// 4 Flags           0         i32 BE; bit0=EMPTY, bit1=ConcurrencyChecks
    /// 5 Count           0         i32 BE = map.Count
    /// 6..5+2N Key/Value 1         DSCode-tagged key, DSCode-tagged value, alternating
    /// </code>
    /// <para>
    /// <b>Part order quirk:</b> there are <i>two</i> int32 parts after
    /// EventId before the count &#x2014; cppcache writes
    /// <c>writeIntPart(0)</c> first (a placeholder where the original
    /// design intended a "skipCallbacks" indicator; cppcache comment
    /// reads <c>// writeIntPart(skipCallBacks ? 0 : 1);</c>), then
    /// <c>writeIntPart(flags)</c>. The placeholder is always <c>0</c>
    /// on the wire; we mirror it byte-for-byte. The flags part carries
    /// the real EMPTY / ConcurrencyChecks bits.
    /// </para>
    /// <para>
    /// <b>Flags semantics</b> (cppcache <c>TcrMessage.cpp:2396-2405</c>):
    /// identical to <see cref="RemoveAll"/> &#x2014; bit 0
    /// (<c>kFlagEmpty=0x01</c>) when <c>caching-enabled=false</c>, bit 1
    /// (<c>kFlagConcurrencyChecks=0x02</c>) when concurrency checks are
    /// on. The server uses these to decide whether to ship versionTags
    /// back in the chunked reply. Phase 1.3 MVP regions don't yet
    /// expose either attribute &#x2014; caller passes <c>0</c>; revisit
    /// when client-side caching lands (Phase 4+).
    /// </para>
    /// <para>
    /// <b>EventId reservation:</b> same scheme as <see cref="RemoveAll"/>.
    /// cppcache calls <c>writeEventIdPart(map.size() - 1)</c>; one
    /// <c>(threadId, baseSeq)</c> pair on the wire, but
    /// <see cref="Internal.EventIdGenerator.NextRange"/> bumps the
    /// per-cache counter by <c>N</c> slots so each entry's logical
    /// event is <c>(clientId, threadId, baseSeq+i)</c> for
    /// <c>i &#x2208; [0, N)</c>.
    /// </para>
    /// <para>
    /// <b>Callback path not exposed:</b> cppcache picks
    /// <see cref="MessageType.PutAllWithCallback"/> (108) when
    /// <c>aCallbackArgument != nullptr</c> (numParts becomes
    /// <c>6+2N</c> with the callback part inserted between Count and
    /// the entries). Phase 1.3 has no <c>PutAllAsync</c> callback
    /// overload on <see cref="IRegion"/>, so the builder parameter
    /// stays default-<c>null</c> &#x2014; we always emit message
    /// type 56. Wired here for forward-compat: if the public surface
    /// ever grows a callback overload, switching the msg type is a
    /// one-line change.
    /// </para>
    /// <para>
    /// <b><c>messageResponseTimeout</c> part</b> cppcache appends when
    /// <c>m_messageResponseTimeout &#x2265; 0</c> (extra trailing
    /// milliseconds-part bumping numParts by 1) is <b>not</b> emitted
    /// &#x2014; cppcache's member initialises to <c>-1</c> and only
    /// newer timeout-aware overloads bump it. Same call as
    /// <see cref="RemoveAll"/> / <see cref="ClearRegion"/>.
    /// </para>
    /// </remarks>
    /// <param name="regionName">Full region path (e.g. <c>"/orders"</c>).</param>
    /// <param name="map">Entries to put. Empty is rejected &#x2014;
    /// cppcache's <c>map.size() - 1</c> reserve underflows on zero and
    /// the round-trip would be a no-op anyway.</param>
    /// <param name="eventThreadId">Thread component of the EventId pair.</param>
    /// <param name="eventSequenceId">Base sequence id; see "EventId
    /// reservation" in remarks.</param>
    /// <param name="callbackArgument">Reserved for a future callback
    /// overload on <see cref="IRegion"/>; Phase 1.3 always
    /// <c>null</c>. Non-null would flip the message type to
    /// <see cref="MessageType.PutAllWithCallback"/> (108) and insert a
    /// callback part &#x2014; not implemented yet, throws when set.</param>
    /// <param name="transactionId">Geode txn id;
    /// <see cref="MetaTransactionId"/> for non-transactional ops.</param>
    public TcrMessage PutAll(
        string regionName,
        IReadOnlyDictionary<object, object> map,
        long eventThreadId,
        long eventSequenceId,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(map);
        if (map.Count == 0)
        {
            throw new ArgumentException(
                "PutAll requires at least one entry.", nameof(map));
        }

        // Phase 1.3: callback overload not exposed on IRegion; the
        // PUT_ALL_WITH_CALLBACK (108) path stays unwired. Refuse rather
        // than silently emitting the wrong msg type if someone tries.
        if (callbackArgument is not null)
        {
            throw new NotSupportedException(
                "PutAll with callback argument is not implemented yet (cppcache "
                + "PUT_ALL_WITH_CALLBACK=108 path). Phase 1.3 only wires the "
                + "no-callback overload.");
        }

        var parts = new List<TcrPart>(5 + map.Count * 2)
        {
            // Part 1 — Region name. Raw ASCII (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — EventId. 18 raw bytes:
            //   [u8 longCode=3][i64 threadId BE][u8 longCode=3][i64 baseSeq BE]
            partBuilder.Raw(w =>
            {
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventThreadId);
                w.WriteByte(EventIdLongCode);
                w.WriteInt64(eventSequenceId);
            }, sizeHint: 18),

            // Part 3 — SkipCallbacks placeholder. cppcache hard-codes 0
            //   (the commented-out line reveals the original design
            //   intended `skipCallBacks ? 0 : 1`, but it was inlined as
            //   a constant). We mirror the constant byte-for-byte.
            partBuilder.Int32(0),

            // Part 4 — Flags (cppcache writeIntPart). Phase 1.3 MVP
            //   always 0 (no client-side caching, no concurrency checks).
            //   Same decision as RemoveAll; revisit Phase 4+.
            partBuilder.Int32(0),

            // Part 5 — Number of entries (cppcache writeIntPart).
            partBuilder.Int32(map.Count),
        };

        // Parts 6..5+2N — Each (key, value) pair, each DSCode-tagged
        // via the registry. cppcache iterates the HashMapOfCacheable
        // and writes the two object parts in iteration order; the
        // server reconstructs the map by pairing consecutive entries.
        foreach (var kv in map)
        {
            ArgumentNullException.ThrowIfNull(kv.Key);
            ArgumentNullException.ThrowIfNull(kv.Value);
            var key = kv.Key;
            var value = kv.Value;
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, key)));
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, value)));
        }

        return new TcrMessage(
            MessageType: MessageType.PutAll,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
