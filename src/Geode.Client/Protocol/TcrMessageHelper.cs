using System.Text;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol;

/// <summary>
/// Static helpers around the chunked-reply wire format. Mirrors
/// cppcache <c>TcrMessageHelper</c>
/// (<c>cppcache/src/TcrMessage.cpp:3181-3251</c>).
/// </summary>
/// <remarks>
/// Phase 1.3.b ??only <see cref="ReadChunkPartHeader"/> is sketched
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
        DataInput reader,
        byte expectedDsCode,
        int expectedPartType,
        string methodName,
        out int partLen,
        byte isLastChunk)
    {
        ArgumentNullException.ThrowIfNull(reader);

        partLen = reader.ReadInt32();
        var isObj = reader.ReadBool();


        if (partLen == 0)
        {
            return ChunkObjectType.NullObject;
        }

      
        if (!isObj)
        {
            logger.LogDebug(
                "TcrMessageHelper::readChunkPartHeader: {MethodName}: part is not object",
                methodName);
            return ChunkObjectType.Exception;
        }

      
        var partType = reader.ReadByte();
        var compId = (int)partType;

        if (partType == DSCode.JavaSerializable)
        {
            logger.LogDebug(
                "TcrMessageHelper::readChunkPartHeader: {MethodName}: " +
                "java-serialised exception chunk",
                methodName);
            return ChunkObjectType.Exception;
        }

       
        if (partType == DSCode.NullObj)
        {
            return ChunkObjectType.NullObject;
        }


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
                compId = (sbyte)reader.ReadByte();
            }
        }

        
        if (compId != expectedPartType)
        {
            throw new GeodeException(
                $"TcrMessageHelper.ReadChunkPartHeader: {methodName}: " +
                $"got unhandled object type = {compId}, " +
                $"expected = {expectedPartType}, raw = {(int)partType}");
        }

     
        _ = isLastChunk;
        return ChunkObjectType.Object;
    }

    /// <summary>
    /// Best-effort ASCII preview of an Exception reply's Part 0. The
    /// server typically returns the Java exception class name + message
    /// there as a <c>CacheableASCIIString</c>; until <c>StringDataConverter</c>
    /// lands we render printable bytes directly so the caller sees a
    /// readable hint in the <see cref="GeodeException"/> message.
    /// </summary>
    /// <remarks>
    /// Mirror of cppcache <c>TcrMessageHelper::readExceptionPart</c>
    /// (<c>cppcache/src/TcrMessage.cpp:3253</c>) — but stripped down:
    /// cppcache actually deserialises the Java exception object, we
    /// just dump printable ASCII for diagnostics. Upgrades when
    /// <c>StringDataConverter</c> + Java exception deserialise land.
    /// </remarks>
    public static string DecodeExceptionPreview(TcrMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Parts.Count == 0)
        {
            return "<no exception parts>";
        }

        var bytes = reply.Parts[0].Payload.Span;
        var sb = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        return sb.ToString();
    }
}
