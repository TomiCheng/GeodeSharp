namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// cppcache hard-codes <c>writeIntPart(15)</c> for part 3
    /// (<c>cppcache/src/TcrMessage.cpp:1793</c> &#x2014; the comment
    /// labels it "X (COMPILE_QUERY_CLEAR_TIMEOUT)"). The identifier
    /// exists only in the comment; the value is a magic number Geode
    /// server interprets as how many seconds the compiled-query cache
    /// entry lives. Phase 1.4 keeps the same constant for wire parity.
    /// </summary>
    private const int CompileQueryClearTimeoutSeconds = 15;

    /// <summary>
    /// Build a <see cref="MessageType.QueryWithParameters"/> (80) request
    /// frame. Mirrors cppcache <c>TcrMessageQueryWithParameters</c>
    /// (<c>cppcache/src/TcrMessage.cpp:1769-1806</c>); the "send + reply"
    /// flow shares
    /// <c>RemoteQuery::executeNoThrow</c>
    /// (<c>cppcache/src/RemoteQuery.cpp:134-157</c>) with <see cref="Query"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.QueryWithParameters"/>=80,
    /// NumParts=3 + (timeout?1:0) + paramCount, TransactionId=-1,
    /// EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part            IsObject  Payload
    /// 1 QueryString     0         raw OQL bytes (cppcache writeRegionPart)
    /// 2 ParamCount      0         i32 BE (cppcache writeIntPart)
    /// 3 CompileTimeout  0         i32 BE = 15 (cppcache hard-coded; see
    ///                             <see cref="CompileQueryClearTimeoutSeconds"/>)
    /// 4 (optional)      0         i32 BE response timeout ms
    /// 5..N Parameters   1         DSCode-tagged serialised bind value
    ///                             (cppcache writeObjectPart per element)
    /// </code>
    /// <para>
    /// <b>Differences from <see cref="Query"/></b>: no EventId part
    /// &#x2014; cppcache <c>TcrMessageQueryWithParameters</c> omits it
    /// (<c>TcrMessage.cpp:1785-1804</c>) where <c>TcrMessageQuery</c>
    /// includes it; the server's
    /// <c>ClientHealthMonitor</c> de-dupe path for parameterised query
    /// is keyed differently upstream (we do not surface that detail).
    /// </para>
    /// <para>
    /// <b>NumParts vs cppcache</b>: cppcache hard-codes
    /// <c>numOfParts = 4 + paramList.size()</c> on
    /// <c>TcrMessage.cpp:1784</c> regardless of whether the timeout
    /// part is actually emitted (the if-check is on line 1796). If a
    /// caller ever passes <c>timeout &lt; 0</c> the header advertises
    /// 4 fixed parts but writes only 3 — a latent wire mismatch.
    /// cppcache callers always pass <c>DEFAULT_QUERY_RESPONSE_TIMEOUT</c>
    /// (15s, positive), so the bug never surfaces. We compute
    /// <c>numOfParts</c> conditionally so the wire byte count always
    /// matches the header.
    /// </para>
    /// <para>
    /// Parameter encoding flows through
    /// <see cref="Serialization.SerializationRegistry.WriteObject"/>:
    /// each element gets its DSCode + body written into a Part with
    /// <c>IsObject=1</c>. <see langword="null"/> entries serialise as
    /// <see cref="DSCode.NullObj"/> (OQL <c>NULL</c>).
    /// </para>
    /// </remarks>
    public TcrMessage QueryWithParameters(
        string queryString,
        IList<object?> parameters,
        int? messageResponseTimeoutMillis = DefaultQueryResponseTimeoutMillis,
        int transactionId = MetaTransactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryString);
        ArgumentNullException.ThrowIfNull(parameters);

        var paramCount = parameters.Count;
        var hasTimeoutPart = messageResponseTimeoutMillis.HasValue;
        var capacity = 3 + (hasTimeoutPart ? 1 : 0) + paramCount;
        var parts = new List<TcrPart>(capacity)
        {
            // Part 1 — Query string. cppcache writeRegionPart of the OQL
            // (it re-uses the region-name part for the OQL body); we
            // call ModifiedUtf8 directly to make the encoding intent
            // explicit — server-side decoder is the same in both cases.
            partBuilder.ModifiedUtf8(queryString),

            // Part 2 — Parameter count (cppcache writeIntPart).
            partBuilder.Int32(paramCount),

            // Part 3 — Server compile-query-cache TTL seconds; cppcache
            // hard-codes 15 (see CompileQueryClearTimeoutSeconds doc).
            partBuilder.Int32(CompileQueryClearTimeoutSeconds),
        };

        // Part 4 — Optional response timeout (cppcache writeMillisecondsPart
        // = writeIntPart). null → omit (cppcache "< 0" branch).
        if (messageResponseTimeoutMillis is { } ms)
        {
            parts.Add(partBuilder.Int32(ms));
        }

        // Part 5..N — Bind parameters in order. Each element is
        // DSCode-tagged via the central registry (handles null →
        // DSCode.NullObj automatically per its contract).
        foreach (var value in parameters)
        {
            parts.Add(partBuilder.Object(w => _serializationRegistry.WriteObject(w, value)));
        }

        return new TcrMessage(
            MessageType: MessageType.QueryWithParameters,
            TransactionId: transactionId,
            EarlyAck: 0,
            Parts: parts);
    }
}
