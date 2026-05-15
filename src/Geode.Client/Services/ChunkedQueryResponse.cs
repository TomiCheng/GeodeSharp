using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// <see cref="TcrChunkedResult"/> consumer for the chunked reply of a
/// <see cref="MessageType.Query"/> / <see cref="MessageType.QueryWithParameters"/>
/// request. Mirrors cppcache <c>ChunkedQueryResponse</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:411-444</c>; impl in
/// <c>cppcache/src/ThinClientRegion.cpp:3291-3480</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.4 status: step-list skeleton.</b>
/// <see cref="HandleChunk"/> / <see cref="Reset"/> /
/// <see cref="ReadObjectPartList"/> / <see cref="SkipClass"/> bodies
/// are C1–C12 / R1–R3 / S1–S4 / K1–K2 step comments + NIE; impl lands
/// step by step.
/// </para>
/// <para>
/// <b>Generic <typeparamref name="T"/></b>: the row type the caller
/// expects (driven by <see cref="IQuery{T}"/>). For <c>SELECT *</c>
/// this is the region's value type; for <c>SELECT COUNT(*)</c> it is
/// typically <see cref="int"/>; multi-column projection
/// (<c>SELECT field1, field2</c>) uses <c>Struct</c>. Whether
/// <typeparamref name="T"/> conversion happens inside this decoder
/// (push directly typed <typeparamref name="T"/>) or in
/// <see cref="Internal.RemoteQuery{T}.ExecuteCoreAsync"/> B10 (push
/// raw <see cref="object"/>?, project at the consumer) is deferred to
/// impl time — both are viable given the cppcache "flat
/// <c>CacheableVector</c>" intermediate.
/// </para>
/// <para>
/// <b>Phase 1.4 vs cppcache scope.</b>
/// </para>
/// <list type="bullet">
///   <item><c>m_queryResults</c> (cppcache <c>CacheableVector</c>)
///         &#x2192; <see cref="Results"/>.</item>
///   <item><c>m_structFieldNames</c> &#x2192;
///         <see cref="StructFieldNames"/>; populated by C7 only when
///         the reply's collection type is
///         <c>org.apache.geode.cache.query.Struct</c>.</item>
///   <item><c>readSecureObjectPart</c> (auth trailer) — Phase 3 scope;
///         C12 leaves it as a no-op while
///         <paramref name="msg"/> is null in Phase 1.4.</item>
/// </list>
/// </remarks>
/// <param name="serviceProvider">DI scope for per-chunk
/// <see cref="BigEndianBinaryReader"/> construction.</param>
/// <param name="logger">Severity-aligned with cppcache LOG* calls.</param>
/// <param name="tcrMessageHelper">Shared chunk-part-header decoder
/// (cppcache <c>readChunkPartHeader</c>).</param>
/// <param name="msg">Reply <see cref="TcrMessage"/> for auth-trailer
/// / pool back-refs. Mirrors cppcache <c>ChunkedQueryResponse::m_msg</c>;
/// Phase 3+ (auth) actually reads it, Phase 1.4 leaves <c>null</c>.</param>
internal sealed class ChunkedQueryResponse<T>(
#pragma warning disable CS9113 // unused while bodies are step-list skeletons
    IServiceProvider serviceProvider,
    ILogger<ChunkedQueryResponse<T>> logger,
    TcrMessageHelper tcrMessageHelper,
    SerializationRegistry serializationRegistry,
    TcrMessage? msg = null) : TcrChunkedResult
#pragma warning restore CS9113
{
    /// <summary>
    /// Typed row accumulator filled by <see cref="HandleChunk"/>
    /// across all chunks. Mirrors cppcache
    /// <c>ChunkedQueryResponse::m_queryResults</c>. For
    /// single-column queries each element is one row value cast to
    /// <typeparamref name="T"/>; for multi-column projection
    /// (<typeparamref name="T"/> = <see cref="QueryStruct"/>) the
    /// decoder groups <c>K</c> field values per row and pushes one
    /// assembled <see cref="QueryStruct"/> per row. Either way the
    /// caller-facing shape is <c>IReadOnlyList&lt;T?&gt;</c>;
    /// <see cref="Internal.RemoteQuery{T}.ExecuteCoreAsync"/> B10
    /// returns this directly.
    /// </summary>
    private readonly List<T?> _results = [];

    /// <summary>
    /// Struct projection field names. Mirrors cppcache
    /// <c>ChunkedQueryResponse::m_structFieldNames</c>. Empty when the
    /// reply is a single-column <c>ResultSet</c>; populated by C7 when
    /// the reply's collection type is
    /// <c>org.apache.geode.cache.query.Struct</c>.
    /// </summary>
    private readonly List<string> _structFieldNames = [];

    public IReadOnlyList<T?> Results => _results;
    public IReadOnlyList<string> StructFieldNames => _structFieldNames;

    // ────────────────────────────────────────────────────────────
    //  Step list for HandleChunk / ReadObjectPartList / SkipClass /
    //  Reset. Mirrors cppcache ChunkedQueryResponse::handleChunk +
    //  readObjectPartList + skipClass + reset
    //  (ThinClientRegion.cpp:3291-3480).
    //
    //  ── Phase 1.4 skipped ──
    //   • readSecureObjectPart (auth trailer) — Phase 3
    //   • cacheImpl / pool back-refs — replaced by DI-injected
    //     SerializationRegistry / BigEndianBinaryReader
    //
    //  ── Open design questions (decide at impl time) ──
    //   • T conversion site:
    //       (a) push object? here, RemoteQuery<T>.B10 projects
    //       (b) push T inline (each readObject cast to T)
    //   • Struct reshape site:
    //       (i)  collector returns flat values; B10 reshapes
    //       (ii) collector reshapes; B10 returns Results directly
    //
    public override void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk)
    {
        // C1 — Log entry. cppcache L3350.
        logger.LogDebug("ChunkedQueryResponse::handleChunk..");

        // C2 — Wrap chunk bytes in BigEndianBinaryReader. cppcache
        //   L3351: createDataInput(chunk, chunkLen, pool). Matches
        //   ChunkedGetAllResponse pattern (DI-built reader so future
        //   per-reader deps flow in without ctor churn).
        var reader = ActivatorUtilities.CreateInstance<BigEndianBinaryReader>(
            serviceProvider, payload);

        // C3 — Read chunk part header. cppcache L3354-3357. Classifier:
        //   • C3a Exception → caller's B7 reply switch throws.
        //   • C3b NullObject → scalar COUNT(*) result follows in a
        //                       fresh part after the null header.
        //   • C3c Object → continue to C4.
        var chunkType = tcrMessageHelper.ReadChunkPartHeader(
            reader,
            DSCode.FixedIDByte,
            (int)DSFid.CollectionTypeImpl,
            nameof(ChunkedQueryResponse<>),
            out var partLen,
            isLastChunk: (byte)(isLastChunk ? 1 : 0));

        if (chunkType == TcrMessageHelper.ChunkObjectType.Exception)
        {
            // C3a — cppcache L3358-3361 reads readSecureObjectPart;
            // Phase 1.4 has no msg → no auth trailer to drain. The
            // chunk-type flip is enough — the dispatcher records the
            // EXCEPTION reply MessageType and RemoteQuery.B7 throws.
            return;
        }

        if (chunkType == TcrMessageHelper.ChunkObjectType.NullObject)
        {
            // C3b — scalar result (SELECT COUNT(*)). cppcache L3362-3370:
            //   the chunked reply ships a fresh part after the null
            //   header carrying the actual Int32 value.
            reader.ReadInt32();         // next-part partLen, ignored
            reader.ReadBool();          // next-part isObj, ignored
            var scalar = serializationRegistry.ReadObject(reader);
            _results.Add((T?)scalar);
            // TODO Phase 3 — m_msg.readSecureObjectPart(reader, ...).
            return;
        }

        // C3c — Object chunk; delegate to the body decoder (C4–C12).
        HandleObjectChunk(reader, partLen);
    }

    /// <summary>
    /// Decode the Object-branch body of a query chunk (C4–C12). Split
    /// out of <see cref="HandleChunk"/> so the Exception / NullObject
    /// short-circuits stay readable. cppcache keeps everything inline
    /// in <c>ChunkedQueryResponse::handleChunk</c>
    /// (<c>ThinClientRegion.cpp:3380-3465</c>); we factor by branch
    /// type for clarity.
    /// </summary>
    /// <param name="reader">Reader positioned right after C3's
    /// <c>readChunkPartHeader</c> consumed the partLen + isObj +
    /// FixedIDByte + DSFid.CollectionTypeImpl prefix.</param>
    /// <param name="partLen">First part's payload length (cppcache
    /// <c>partLen</c>). Used by C8 to advance past the metadata part
    /// once C7 has extracted any Struct field names.</param>
    private void HandleObjectChunk(BigEndianBinaryReader reader, int partLen)
    {
        // C4 — Skip the outer collection type's parent-class header
        // (cppcache L3380's skipClass). server tags the wrapper as
        // HashSet / StructSet here; Phase 1.4 doesn't need to
        // distinguish — the inner class name (C6) carries the real
        // ResultSet vs StructSet discriminator. cppcache:
        //   // ignoring parent classes for now
        //   // we will require to look at it once CQ is to be implemented.
        //   skipClass(input);
        SkipClass(reader);

        // C5 — Consume the fixed 3-byte preamble of the inner class
        //   header. cppcache L3384-3386 reads and discards each:
        //     input.read();  // FixedIDByte (1)
        //     input.read();  // DataSerializable (45)
        //     input.read();  // Class (43)
        //   No assertion in cppcache; a mismatch surfaces as garbage
        //   bytes in C6's readString below. Same trust-the-server
        //   posture here — defensive validation can land if
        //   integration tests show server quirks.
        reader.ReadByte();      // DSCode.FixedIDByte
        reader.ReadByte();      // DSCode.DataSerializable
        reader.ReadByte();      // DSCode.Class

        // C6 — Read collection type name string. cppcache L3387:
        //   const auto isStructTypeImpl = input.readString();
        // Server uses CacheableASCIIString (87) for pure-ASCII class
        // names; CacheableString (42) for non-ASCII. ReadShortString
        // dispatches both forms.
        var collectionTypeName = ReadShortString(reader, "collection type name");

        // C7 — If type is the Java Struct, capture column field names.
        //   cppcache L3389-3401. Cross-chunk dedup: server may resend
        //   the same field-name list in every chunk; we keep only the
        //   first chunk's copy.
        if (collectionTypeName == "org.apache.geode.cache.query.Struct")
        {
            // Phase 1.4 — StructSet wire shape requires T = QueryStruct.
            // NewQuery's type guard accepts any wire-registered T or
            // QueryStruct; the runtime mismatch (e.g. caller wrote
            // IQuery<int> for "SELECT id, name") can only be detected
            // here, when server's collection type is observed.
            if (typeof(T) != typeof(QueryStruct))
            {
                throw new GeodeException(
                    $"Server returned a multi-column projection but " +
                    $"IQuery<{typeof(T).Name}> is not IQuery<{nameof(QueryStruct)}>.");
            }

            var numOfFldNames = reader.ReadArrayLength();
            var skipDup = _structFieldNames.Count != 0;
            for (var i = 0; i < numOfFldNames; i++)
            {
                // Field name uses the same short-string forms as C6.
                var fieldName = ReadShortString(reader, "struct field name");
                if (!skipDup)
                {
                    _structFieldNames.Add(fieldName);
                }
            }
        }

        // C8 — Skip remaining bytes in the first (metadata) part.
        //   cppcache L3404-3406: input.reset(); advanceCursor(partLen + 5).
        //   Our AdvanceCursor is forward-only; C3-C7 read strictly
        //   within the first part body (Position ≤ partLen + 5), so
        //   the equivalent is a positive delta to the absolute target.
        //   5 = i32 partLen header (4) + isObj byte (1).
        var firstPartEnd = partLen + 5;
        reader.AdvanceCursor(firstPartEnd - reader.Position);

        // C9 — Read second (data) part header. cppcache L3408-3417:
        //   input.readInt32();  // skip part length
        //   if (!input.read()) throw MessageException(...)
        // We follow cppcache and drop the second part's length —
        // bounds checking via the chunk-level length is enough.
        reader.ReadInt32();
        var isObj = reader.ReadBool();
        if (!isObj)
        {
            throw new GeodeException(
                "Query response part is not an object; possible serialization mismatch.");
        }

        // C10 — isResultSet = (_structFieldNames.Count == 0).
        //   cppcache L3419. C7 only fills _structFieldNames when the
        //   collection type was the Java Struct, so empty = single-
        //   column ResultSet path; non-empty = multi-column StructSet.
        var isResultSet = _structFieldNames.Count == 0;

        // C11 — Read array type DSCode + branch to C11a / C11b / C11c.
        //   cppcache L3421: auto arrayType = static_cast<DSCode>(input.read());
        var arrayType = reader.ReadByte();

        if (arrayType == DSCode.CacheableObjectArray)
        {
            // C11a — Object[] inline. cppcache L3423-3440.
            //   readArrayLength → arraySize
            //   skipClass
            //   foreach of arraySize:
            //     if isResultSet: readObject → push
            //     else (StructSet): read 1 byte (skip),
            //                        readArrayLength → arraySize2,
            //                        skipClass,
            //                        foreach: readObject → push
            //                        (flattens N rows × K cols into a linear list)
            var arraySize = reader.ReadArrayLength();

            // skipClass — array's element-type class metadata.
            //   cppcache L3425. SkipClass body itself still NIE; the
            //   call site is wired for when S1-S4 lands.
            SkipClass(reader);

            // Row loop. cppcache L3426-3440.
            for (var arrayItem = 0; arrayItem < arraySize; arrayItem++)
            {
                if (isResultSet)
                {
                    // ResultSet — one row = one value. cppcache:
                    //   input.readObject(value);
                    //   m_queryResults->push_back(value);
                    var value = serializationRegistry.ReadObject(reader);
                    _results.Add((T?)value);
                }
                else
                {
                    // StructSet — one row = inner array of K field
                    // values. cppcache flattens into m_queryResults;
                    // we assemble a QueryStruct inline (Option C) and
                    // push one row at a time. T == QueryStruct
                    // verified by C7's guard.
                    //
                    // cppcache L3432-3439:
                    //   input.read();                          // marker, skip
                    //   int32_t arraySize2 = input.readArrayLength();
                    //   skipClass(input);
                    //   foreach: input.readObject(value); push to flat list
                    reader.ReadByte();
                    var k = reader.ReadArrayLength();
                    SkipClass(reader);
                    var fieldValues = new List<object?>(k);
                    for (var j = 0; j < k; j++)
                    {
                        fieldValues.Add(serializationRegistry.ReadObject(reader));
                    }
                    var row = new QueryStruct(_structFieldNames, fieldValues);
                    _results.Add((T?)(object?)row);
                }
            }
        }
        else if (arrayType == DSCode.FixedIDByte)
        {
            // C11b — CacheableObjectPartList framed.
            //   cppcache L3441-3454: read FID byte; expect
            //   CacheableObjectPartList (25); delegate to readObjectPartList.
            var fid = reader.ReadByte();
            if (fid != (byte)DSFid.CacheableObjectPartList)
            {
                throw new GeodeException(
                    $"ChunkedQueryResponse: expected CacheableObjectPartList " +
                    $"({(int)DSFid.CacheableObjectPartList}) inside FixedIDByte " +
                    $"frame, got FID {(sbyte)fid}.");
            }
            ReadObjectPartList(reader, isResultSet);
        }
        else
        {
            // C11c — unknown array type. cppcache L3455-3463.
            throw new GeodeException(
                $"ChunkedQueryResponse: unhandled message format DSCode {arrayType}; " +
                "possible serialization mismatch.");
        }

        // C12 — Drain auth trailer. cppcache L3465:
        //   m_msg.readSecureObjectPart(input, false, true, isLastChunkWithSecurity)
        // Phase 1.4 no-op (msg = null). Phase 3 ports
        // TcrMessage.ReadSecureObjectPart + wires it in here + C3a / C3b.
    }

    public override void Reset()
    {
        // K1 — _results.Clear()         cppcache L3292
        // K2 — _structFieldNames.Clear() cppcache L3293
        _results.Clear();
        _structFieldNames.Clear();
    }

    /// <summary>
    /// cppcache <c>ChunkedQueryResponse::readObjectPartList</c>
    /// (<c>ThinClientRegion.cpp:3296-3345</c>). Recursive: StructSet
    /// nesting re-enters with <paramref name="isResultSet"/>=<see langword="true"/>.
    /// </summary>
    private void ReadObjectPartList(BigEndianBinaryReader reader, bool isResultSet)
    {
        // R1 — readBoolean — must be false. cppcache L3298-3301:
        //   if (input.readBoolean()) throw IllegalStateException(
        //       "Query response has keys which is unexpected.");
        if (reader.ReadBool())
        {
            throw new GeodeException(
                "ChunkedQueryResponse::readObjectPartList: " +
                "query response has keys which is unexpected.");
        }

        // R2 — readInt32 → len. cppcache L3303:
        //   int32_t len = input.readInt32();
        var len = reader.ReadInt32();

        // R3 — for each entry, read tag byte and branch:
        //   • tag == 2 → per-entry exception → throw
        //   • else if isResultSet → readObject, push single value
        //   • else → read inner CacheableObjectPartList of K fields,
        //            assemble QueryStruct, push as T?
        // cppcache L3305-3344. The cppcache "recurse with
        // isResultSet=true" path flattens K field values into the
        // shared accumulator; we instead read them into a local
        // buffer and build QueryStruct per row (Option C).
        for (var i = 0; i < len; i++)
        {
            var tag = reader.ReadByte();
            if (tag == 2)
            {
                // R3a — per-entry exception. cppcache L3306-3309.
                ReadExceptionAndThrow(reader);
            }

            if (isResultSet)
            {
                // R3b — single value row.
                var value = serializationRegistry.ReadObject(reader);
                _results.Add((T?)value);
            }
            else
            {
                // R3c — inner CacheableObjectPartList = one Struct row.
                // T == QueryStruct enforced by C7 (HandleObjectChunk).
                // cppcache L3315-3341.
                var dscode = reader.ReadByte();
                if (dscode != DSCode.FixedIDByte)
                {
                    throw new GeodeException(
                        $"ChunkedQueryResponse: expected FixedIDByte for inner " +
                        $"struct-row marker, got DSCode {dscode}.");
                }
                var fid = reader.ReadByte();
                if (fid != (byte)DSFid.CacheableObjectPartList)
                {
                    throw new GeodeException(
                        $"ChunkedQueryResponse: expected CacheableObjectPartList " +
                        $"({(int)DSFid.CacheableObjectPartList}) for inner struct-row, " +
                        $"got FID {(sbyte)fid}.");
                }

                var fieldValues = ReadStructRow(reader);
                var row = new QueryStruct(_structFieldNames, fieldValues);
                _results.Add((T?)(object?)row);
            }
        }
    }

    /// <summary>
    /// Read an inner <c>CacheableObjectPartList</c> as one Struct row:
    /// the K field values that follow the row's outer marker bytes.
    /// Cppcache recurses <see cref="ReadObjectPartList"/> with
    /// <c>isResultSet=true</c> which flattens K values into
    /// <c>m_queryResults</c>; we use a local buffer so caller can
    /// build a single <see cref="QueryStruct"/> per row.
    /// </summary>
    private List<object?> ReadStructRow(BigEndianBinaryReader reader)
    {
        // Inner header: readBoolean keys-flag (must be false) +
        // readInt32 K. Mirrors cppcache readObjectPartList opening.
        if (reader.ReadBool())
        {
            throw new GeodeException(
                "ChunkedQueryResponse::readObjectPartList (inner): " +
                "query response has keys which is unexpected.");
        }
        var k = reader.ReadInt32();

        var values = new List<object?>(k);
        for (var j = 0; j < k; j++)
        {
            var tag = reader.ReadByte();
            if (tag == 2)
            {
                // Per-field exception. Same encoding as outer R3a.
                ReadExceptionAndThrow(reader);
            }
            values.Add(serializationRegistry.ReadObject(reader));
        }
        return values;
    }

    /// <summary>
    /// Handle the per-entry exception form. cppcache
    /// <c>ChunkedQueryResponse::readObjectPartList</c> L3306-3309:
    /// skip the type-metadata blob (<c>advanceCursor(readArrayLength())</c>)
    /// then <c>readString</c> for the message and
    /// <c>throw IllegalStateException(msg)</c>. We accept only
    /// <see cref="DSCode.CacheableString"/> for the message (Huge /
    /// ASCII variants would also be valid cppcache wire shapes but
    /// servers seldom use them for exception text).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ReadExceptionAndThrow(BigEndianBinaryReader reader)
    {
        reader.AdvanceCursor(reader.ReadArrayLength());
        var msg = ReadShortString(reader, "per-entry exception message");
        throw new GeodeException(
            $"ChunkedQueryResponse: server-side per-entry exception: {msg}");
    }

    /// <summary>
    /// Read a DSCode-tagged short string. cppcache
    /// <c>DataInput::readString</c> (<c>DataInput.hpp:280-293</c>)
    /// dispatches on the DSCode tag to four readers; this helper
    /// covers the two short forms <see cref="DSCode.CacheableString"/>
    /// and <see cref="DSCode.CacheableASCIIString"/>. They share the
    /// same wire prefix (u16 length + N bytes), and modified UTF-8
    /// decoding of ASCII bytes is byte-identical to ASCII decoding,
    /// so a single
    /// <see cref="BigEndianBinaryReader.ReadJavaModifiedUtf8"/> call
    /// works for both. Huge variants land with the Phase 4 large-string
    /// reader.
    /// </summary>
    private static string ReadShortString(BigEndianBinaryReader reader, string context)
    {
        var dscode = reader.ReadByte();
        if (dscode == DSCode.CacheableString || dscode == DSCode.CacheableASCIIString)
        {
            return reader.ReadJavaModifiedUtf8();
        }
        throw new GeodeException(
            $"ChunkedQueryResponse: expected CacheableString (42) or " +
            $"CacheableASCIIString (87) for {context}, got DSCode {dscode}.");
    }

    /// <summary>
    /// cppcache <c>ChunkedQueryResponse::skipClass</c>
    /// (<c>ThinClientRegion.cpp:3468-3480</c>). Skips a Java
    /// <c>Class</c> header in the wire stream.
    /// </summary>
    private static void SkipClass(BigEndianBinaryReader reader)
    {
        // S1 — read DSCode; expect Class (43); else throw. cppcache
        //   L3469-3478:
        //     auto classByte = static_cast<DSCode>(input.read());
        //     if (classByte != DSCode::Class) throw IllegalStateException(...);
        var classByte = reader.ReadByte();
        if (classByte != DSCode.Class)
        {
            throw new GeodeException(
                $"ChunkedQueryResponse::skipClass: did not get expected class " +
                $"header byte, got DSCode {classByte}.");
        }

        // S2 — read 1 byte (string type id; ignored, assume normal
        //   string < 64k). cppcache L3472:
        //     // ignore string type id - assuming its a normal (under 64k) string.
        //     input.read();
        reader.ReadByte();

        // S3 — readInt16 → classLen. cppcache L3473:
        //     uint16_t classLen = input.readInt16();
        // cppcache casts the i16 to u16; we read u16 directly. Class
        // name length is unsigned (can't be negative) — same wire
        // bytes either way for the < 32k common case.
        var classLen = reader.ReadUInt16();

        // S4 — advance past the class name bytes. cppcache L3474:
        //     input.advanceCursor(classLen);
        reader.AdvanceCursor(classLen);
    }
}
