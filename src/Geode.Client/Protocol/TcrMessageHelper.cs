using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol;

/// <summary>
/// Static helpers around the chunked-reply wire format. Mirrors
/// cppcache <c>TcrMessageHelper</c>
/// (<c>cppcache/src/TcrMessage.cpp:3181-3251</c>).
/// </summary>
/// <remarks>
/// Phase 1.3.b — only <see cref="ReadChunkPartHeader"/> is sketched
/// (NIE body). cppcache's other helpers
/// (<c>readExceptionPart</c> / <c>skipParts</c>) land when their
/// callers do.
/// </remarks>
internal sealed class TcrMessageHelper(ILogger<TcrMessageHelper> logger)
{
    /// <summary>
    /// Chunk-part dispatch result from <see cref="ReadChunkPartHeader"/>.
    /// Mirrors cppcache <c>TcrMessageHelper::ChunkObjectType</c>.
    /// </summary>
    public enum ChunkObjectType
    {
        /// <summary>Empty chunk &#x2014; server has no result to ship.</summary>
        NullObject,

        /// <summary>Standard object chunk; caller decodes per its
        /// expected DSFid (e.g. <c>VersionedObjectPartList</c>).</summary>
        Object,

        /// <summary>Server-side exception encoded as a Java-serialised
        /// blob. Caller throws.</summary>
        Exception,

        /// <summary>
        /// Single-hop PR-metadata refresh prelude &#x2014; 2 raw bytes
        /// <c>[metadataVersion][networkHopType]</c>. cppcache returns
        /// this via the second overload of <c>readChunkPartHeader</c>;
        /// we collapse into one enum so the caller can switch on a
        /// single value. Phase 4+ work.
        /// </summary>
        Bytes,
    }

    /// <summary>
    /// Read a chunk's leading part header and classify the chunk
    /// shape. Mirrors cppcache
    /// <c>TcrMessageHelper::readChunkPartHeader</c>
    /// (<c>cppcache/src/TcrMessage.cpp:3191-3251</c>).
    /// </summary>
    /// <param name="reader">Reader positioned at the start of the chunk.</param>
    /// <param name="expectedDsCode">Expected leading
    /// <see cref="DSCode"/> for the OBJECT path (e.g.
    /// <see cref="DSCode.FixedIDByte"/>).</param>
    /// <param name="expectedPartType">Expected fixed id following the
    /// DSCode (e.g. <c>(int)DSFid.VersionedObjectPartList</c>).</param>
    /// <param name="methodName">Caller name; goes into log /
    /// exception messages.</param>
    /// <param name="partLen">Out: the i32 partLen prefix the reader
    /// just consumed.</param>
    /// <param name="isLastChunk">Flags byte from the chunk header
    /// (only consumed via <c>readExceptionPart</c> on the EXCEPTION
    /// branch).</param>
    public ChunkObjectType ReadChunkPartHeader(
        BigEndianBinaryReader reader,
        byte expectedDsCode,
        int expectedPartType,
        string methodName,
        out int partLen,
        byte isLastChunk)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Mirrors cppcache TcrMessageHelper::readChunkPartHeader
        // (cppcache/src/TcrMessage.cpp:3191-3251).
        //
        // ─── Step 1: read partLen + isObj ──────────────────────
        partLen = reader.ReadInt32();
        var isObj = reader.ReadBool();

        // ─── Step 2: partLen == 0 → NullObject ─────────────────
        // cppcache comment: "special null object is case for scalar
        // query result". Phase 1.3 ChunkedRemoveAllResponse uses
        // this to recognise an empty-batch reply.
        if (partLen == 0)
        {
            return ChunkObjectType.NullObject;
        }

        // ─── Step 3: !isObj → Exception ────────────────────────
        // cppcache: "otherwise we're currently always expecting an
        // object" — non-object part with non-zero length signals
        // an exception payload.
        if (!isObj)
        {
            logger.LogDebug(
                "TcrMessageHelper::readChunkPartHeader: {MethodName}: part is not object",
                methodName);
            return ChunkObjectType.Exception;
        }

        // ─── Step 4: read DSCode byte ──────────────────────────
        // cppcache reads the byte twice into rawByte / partType
        // (latter cast to DSCode); our DSCode is a byte-constant
        // class so no cast needed. compId defaults to partType and
        // gets overwritten in step 7's FixedIDByte/FixedIDShort
        // branches with the trailing 1- or 2-byte fixed-id.
        var partType = reader.ReadByte();
        var compId = (int)partType;

        // ─── Step 5: JavaSerializable → Exception ──────────────
        // cppcache rewinds (input.reset) + calls readExceptionPart to
        // decode the Java-serialised exception body and mutates the
        // reply msg type to EXCEPTION. Our record is immutable so we
        // can't propagate the type change that way; the body decode
        // also requires a Java exception deserialiser we don't have
        // (Phase 2+ PDX territory). Phase 1.3 just signals Exception
        // back to the caller, which throws GeodeException via the
        // unhandled-chunkType path in ChunkedRemoveAllResponse.
        if (partType == DSCode.JavaSerializable)
        {
            logger.LogDebug(
                "TcrMessageHelper::readChunkPartHeader: {MethodName}: " +
                "java-serialised exception chunk",
                methodName);
            return ChunkObjectType.Exception;
        }

        // ─── Step 6: NullObj DSCode → NullObject ───────────────
        // cppcache comment: "special null object is case for scalar
        // query result". Same NullObject signal as step 2 but
        // triggered by the inner DSCode tag rather than partLen=0.
        if (partType == DSCode.NullObj)
        {
            return ChunkObjectType.NullObject;
        }

        // ─── Step 7: enforce DSCode + read fixed-id compId ─────
        // When caller passed a specific expected DSCode (Byte / Short
        // fixed-id), verify partType matches and read the trailing
        // 1/2-byte fixed-id into compId. expectedDsCode == 0
        // (FixedIDDefault) means "any DSCode is fine"; skip whole
        // block.
        if (expectedDsCode > DSCode.FixedIDDefault)
        {
            if (partType != expectedDsCode)
            {
                throw new GeodeException(
                    $"TcrMessageHelper.ReadChunkPartHeader: {methodName}: " +
                    $"got unhandled object class = {(sbyte)partType}");
            }
            if (expectedDsCode == DSCode.FixedIDShort)
            {
                compId = reader.ReadInt16();
            }
            else if (expectedDsCode == DSCode.FixedIDByte)
            {
                // DSFid is a signed byte on the wire; cppcache reads it
                // via int8_t. Without the (sbyte) cast 0xC5 reads back
                // as 197 (unsigned) instead of -59 (CollectionTypeImpl),
                // breaking the compId compare. Only matters for negative
                // DSFid IDs — the positive ones (VersionedObjectPartList,
                // CacheableObjectPartList, etc.) round-trip either way.
                compId = (sbyte)reader.ReadByte();
            }
        }

        // ─── Step 8: compId mismatch → throw ───────────────────
        if (compId != expectedPartType)
        {
            throw new GeodeException(
                $"TcrMessageHelper.ReadChunkPartHeader: {methodName}: " +
                $"got unhandled object type = {compId}, " +
                $"expected = {expectedPartType}, raw = {(int)partType}");
        }

        // ─── Step 9: standard object chunk ─────────────────────
        // isLastChunk byte unused in our port — cppcache only reads
        // it via readExceptionPart (step 5 deferred) and the secure
        // trailer (Phase 3+ auth).
        _ = isLastChunk;
        return ChunkObjectType.Object;
    }
}
