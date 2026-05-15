using Geode.Client.Protocol;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// <see cref="TcrChunkedResult"/> consumer for the chunked reply of a
/// <see cref="MessageType.Query"/> / <see cref="MessageType.QueryWithParameters"/>
/// request. Mirrors cppcache <c>ChunkedQueryResponse</c>
/// (<c>cppcache/src/ThinClientRegion.hpp:411-444</c>; impl in
/// <c>ThinClientRegion.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1.4 status: empty skeleton.</b> <see cref="HandleChunk"/> /
/// <see cref="Reset"/> throw <see cref="NotImplementedException"/>;
/// <see cref="Results"/> / <see cref="StructFieldNames"/> return empty
/// accumulators until the decoder body lands.
/// </para>
/// <para>
/// <b>Generic <typeparamref name="T"/></b>: the row type the caller
/// expects (driven by <see cref="IQuery{T}"/>). For <c>SELECT *</c>
/// this is the region's value type; for <c>SELECT COUNT(*)</c> it is
/// typically <see cref="int"/>; Phase 2 multi-column projection will
/// surface a <c>Struct</c> row type. Decoder converts each raw row
/// value to <typeparamref name="T"/> on the fly (TypedResultAdapter
/// or per-element cast — detail decided when the body lands).
/// </para>
/// <para>
/// <b>Phase 1.4 vs cppcache scope.</b>
/// </para>
/// <list type="bullet">
///   <item><c>m_queryResults</c> (cppcache <c>CacheableVector</c>)
///         &#x2192; <see cref="Results"/> typed as
///         <see cref="IReadOnlyList{T}"/>.</item>
///   <item><c>m_structFieldNames</c> &#x2192;
///         <see cref="StructFieldNames"/>; populated only by multi-column
///         projection (Phase 2 StructSet), always empty in Phase 1.4.</item>
///   <item><c>skipClass</c> / <c>readObjectPartList</c> — cppcache
///         private helpers; will land as private methods alongside
///         <see cref="HandleChunk"/> when the decoder body fills in.</item>
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
#pragma warning disable CS9113 // unused while HandleChunk is a stub
    IServiceProvider serviceProvider,
    ILogger<ChunkedQueryResponse<T>> logger,
    TcrMessageHelper tcrMessageHelper,
    TcrMessage? msg = null) : TcrChunkedResult
#pragma warning restore CS9113
{
    /// <summary>
    /// Row accumulator filled by <see cref="HandleChunk"/> across all
    /// chunks. Mirrors cppcache
    /// <c>ChunkedQueryResponse::m_queryResults</c>
    /// (<c>std::shared_ptr&lt;CacheableVector&gt;</c>). Caller
    /// (<see cref="Internal.RemoteQuery{T}.ExecuteCoreAsync"/>) reads it
    /// after dispatch returns and surfaces through
    /// <see cref="IQuery{T}.ExecuteAsync(CancellationToken)"/> as
    /// <see cref="IReadOnlyList{T}"/>.
    /// </summary>
    private readonly List<T> _results = [];

    /// <summary>
    /// Struct projection field names. Mirrors cppcache
    /// <c>ChunkedQueryResponse::m_structFieldNames</c>. Empty in Phase
    /// 1.4 (<c>SELECT *</c> / <c>SELECT COUNT(*)</c> are single-column);
    /// populated by the Phase 2 StructSet path.
    /// </summary>
    private readonly List<string> _structFieldNames = [];

    public IReadOnlyList<T> Results => _results;
    public IReadOnlyList<string> StructFieldNames => _structFieldNames;

    public override void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk)
    {
        // Phase 1.4 next step — decode chunk per cppcache
        // ChunkedQueryResponse::handleChunk:
        //   • readChunkPartHeader → classify Object / Exception
        //   • read row values (skipClass + readObjectPartList) into _results
        //   • read structFieldNames (StructSet path, Phase 2)
        throw new NotImplementedException(
            "Phase 1.4 — chunk decoder body pending.");
    }

    public override void Reset()
    {
        // Mirrors cppcache ChunkedQueryResponse::reset — drop partial
        // state from a prior attempt on a different endpoint.
        throw new NotImplementedException(
            "Phase 1.4 — chunk decoder body pending.");
    }
}
