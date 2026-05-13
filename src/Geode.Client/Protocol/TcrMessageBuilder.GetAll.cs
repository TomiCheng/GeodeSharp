namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.GetAll70"/> (100) request frame.
    /// Mirrors cppcache <c>TcrMessageGetAll</c> ctor +
    /// <c>InitializeGetallMsg</c>
    /// (<c>cppcache/src/TcrMessage.cpp:2470-2523</c>); the "send +
    /// chunked reply" flow lives in
    /// <c>ThinClientRegion::getAllNoThrow_remote</c>
    /// (<c>cppcache/src/ThinClientRegion.cpp:1089-1172</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout &#x2014; Header (<see cref="MessageType.GetAll70"/>=100,
    /// NumParts=3, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part         IsObject  Payload
    /// 1 Region       0         raw region path bytes (ASCII; no DSCode)
    /// 2 Keys         1         DSCode.CacheableObjectArray=52
    ///                          + ArrayLen (1/3/5-byte VL)
    ///                          + DSCode.Class=43
    ///                          + writeString("java.lang.Object")
    ///                          + N × writeObject(key)  (each DSCode-tagged)
    /// 3 Callback     1 or 0    DSCode-tagged callback object (IsObject=1),
    ///                          OR i32 BE = 0 (IsObject=0) when no callback
    /// </code>
    /// <para>
    /// <b>Part 2 layout is hand-written, not "wrap the keys in a
    /// CacheableObjectArray and serialise".</b> cppcache has a 4-arg
    /// <c>writeObjectPart</c> overload it labels "will do manually"
    /// (<c>TcrMessage.cpp:2516</c>) precisely because the in-band
    /// wire format of a <c>CacheableObjectArray</c> already matches
    /// what GetAll needs &#x2014; one DSCode byte + array length +
    /// Java class header (<c>DSCode.Class</c> +
    /// <c>"java.lang.Object"</c>) + per-element DSCode-tagged
    /// objects. We mirror the inline pattern so the wire bytes are
    /// obvious here and the builder doesn't carry a hidden dependency
    /// on <c>ObjectArrayDataConverter</c>'s output. Both paths
    /// produce the same bytes by construction; tests cover that.
    /// </para>
    /// <para>
    /// <b>Part 3 conditional shape.</b> When the caller supplies a
    /// non-null callback, Part 3 is a DSCode-tagged object part
    /// (<c>IsObject=1</c>); when not, it's a plain i32 zero
    /// (<c>IsObject=0</c>) &#x2014; cppcache <c>writeIntPart(0)</c>.
    /// The two shapes are <i>not</i> interchangeable: the server
    /// reads Part 3 differently depending on whether the message type
    /// is <see cref="MessageType.GetAll70"/> (no callback) or
    /// <see cref="MessageType.GetAllWithCallback"/> (with callback).
    /// </para>
    /// <para>
    /// <b>Callback path not exposed:</b> Phase 1.3 has no
    /// <c>GetAllAsync</c> callback overload on <see cref="IRegion"/>.
    /// The <paramref name="callbackArgument"/> parameter is kept for
    /// forward-compat symmetry with <see cref="PutAll"/> /
    /// <see cref="RemoveAll"/>; non-null throws
    /// <see cref="NotSupportedException"/>. cppcache flips the msg
    /// type to <see cref="MessageType.GetAllWithCallback"/> (107)
    /// when set; wiring lands when the public overload does.
    /// </para>
    /// <para>
    /// <b>No EventId.</b> Unlike <see cref="PutAll"/> / <see cref="RemoveAll"/>,
    /// GetAll has no per-key event id concept (it's read-only on the
    /// server side &#x2014; no mutation to dedup). The
    /// <see cref="Internal.EventIdGenerator"/> isn't touched by this
    /// path.
    /// </para>
    /// </remarks>
    /// <param name="regionName">Full region path (e.g. <c>"/orders"</c>).</param>
    /// <param name="keys">Keys to fetch. Empty is rejected. Caller
    /// supplies positional access (<see cref="IReadOnlyList{T}"/>)
    /// because the server's chunked reply indexes back into this list
    /// via <c>Keys[index + keysOffset]</c>; the chunked-response
    /// handler reuses the same list reference for that lookup.</param>
    /// <param name="callbackArgument">Reserved for a future callback
    /// overload on <see cref="IRegion"/>; Phase 1.3 always
    /// <c>null</c>. Non-null throws — see remarks.</param>
    /// <param name="transactionId">Geode txn id;
    /// <see cref="MetaTransactionId"/> for non-transactional ops.</param>
    public TcrMessage GetAll(
        string regionName,
        IReadOnlyList<object> keys,
        object? callbackArgument = null,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(regionName);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException(
                "GetAll requires at least one key.", nameof(keys));
        }

        // Phase 1.3: callback overload not exposed on IRegion; refuse
        // rather than silently emitting the wrong msg type
        // (GET_ALL_WITH_CALLBACK=107) if someone tries.
        if (callbackArgument is not null)
        {
            throw new NotSupportedException(
                "GetAll with callback argument is not implemented yet (cppcache "
                + "GET_ALL_WITH_CALLBACK=107 path). Phase 1.3 only wires the "
                + "no-callback overload.");
        }

        // Snapshot the key list into a local so the lambdas below capture
        // a stable reference (defensive — caller could in theory mutate
        // IReadOnlyList<object> if the underlying is a List<object>).
        // Per-key null check up front so the wire writer doesn't blow up
        // half-way through serialisation.
        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] is null)
            {
                throw new ArgumentException(
                    $"GetAll: keys[{i}] is null; null keys are not permitted.",
                    nameof(keys));
            }
        }

        var parts = new List<TcrPart>(3)
        {
            // Part 1 — Region name. Raw ASCII (cppcache writeRegionPart).
            partBuilder.RegionName(regionName),

            // Part 2 — Keys, as the in-band wire shape of a
            // CacheableObjectArray. Mirrors cppcache's manual write
            // (TcrMessage.cpp:702-710) byte-for-byte; we keep it inline
            // here rather than routing through SerializationRegistry +
            // ObjectArrayDataConverter so the wire bytes are visible.
            partBuilder.Object(w =>
            {
                w.WriteByte(DSCode.CacheableObjectArray);
                w.WriteArrayLen(keys.Count);
                w.WriteByte(DSCode.Class);
                w.WriteString(GetAllJavaObjectClassName);
                foreach (var key in keys)
                {
                    _serializationRegistry.WriteObject(w, key);
                }
            }),

            // Part 3 — Callback or int(0). cppcache InitializeGetallMsg
            // (TcrMessage.cpp:2517-2521) dispatches: callback != null →
            // writeObjectPart; null → writeIntPart(0). Phase 1.3 always
            // hits the int(0) branch because we refuse callback above.
            partBuilder.Int32(0),
        };

        return new TcrMessage(
            MessageType: MessageType.GetAll70,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }

    /// <summary>
    /// Java class-name string that goes inside the GetAll keys part.
    /// cppcache hard-codes <c>"java.lang.Object"</c>
    /// (<c>TcrMessage.cpp:707</c>) regardless of the actual element
    /// types &#x2014; the wire's per-element DSCode tells the server
    /// how to deserialise each slot, so the class name is
    /// informational only. Mirrors
    /// <c>ObjectArrayDataConverter.JavaObjectClassName</c>; kept as a
    /// separate constant here so the builder is self-contained.
    /// </summary>
    private const string GetAllJavaObjectClassName = "java.lang.Object";
}
