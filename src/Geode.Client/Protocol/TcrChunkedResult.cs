/*
namespace Geode.Client.Protocol;

/// <summary>
/// Per-request accumulator for the chunks of a chunked TCR reply.
/// Mirrors cppcache <c>TcrChunkedResult</c>
/// (<c>cppcache/src/TcrChunkedContext.hpp:37-114</c>).
/// </summary>
/// <remarks>
/// <para>
/// Some reply types (RemoveAll, PutAll, GetAll70, Query, registerInterest,
/// executeFunction…) come back across multiple chunks rather than as a
/// single <see cref="TcrMessage"/>. The dispatcher reads each chunk's
/// header to learn the payload length + <c>isLastChunk</c> flag, then
/// hands the body to a result instance the caller registered with the
/// request. The result accumulates whatever state it needs (a list of
/// objects, a count, etc.) and exposes the final value to the caller
/// once the dispatcher signals completion.
/// </para>
/// <para>
/// <b>What we drop from cppcache.</b>
/// </para>
/// <list type="bullet">
///   <item><c>finalize</c> / <c>waitFinalize</c> /
///         <c>binary_semaphore</c> — cppcache uses a semaphore to
///         shuttle control back to the calling thread after a worker
///         thread finishes draining chunks. .NET <see cref="Task"/> /
///         <see cref="System.Threading.Tasks.TaskCompletionSource"/>
///         replaces it; signalling lives on the dispatcher, not on
///         the result.</item>
///   <item><c>m_ex</c> / <c>setException</c> / <c>getException</c> —
///         cppcache stashes exceptions caught during chunk processing
///         so the worker thread can surface them later. With async/await
///         exceptions bubble out of <see cref="HandleChunk"/> directly;
///         the dispatcher converts them into a faulted <see cref="Task"/>.</item>
///   <item><c>m_dsmemId</c> / <c>setEndpointMemId</c> — single-hop /
///         PR-metadata plumbing (Phase 4). Add the slot when the
///         metadata-refresh path lands.</item>
/// </list>
/// <para>
/// <b>Lifecycle.</b> One result instance per request. The dispatcher
/// calls <see cref="Reset"/> before the first chunk of a fresh attempt
/// (clears any partial state from a prior failed try on a different
/// endpoint) and then <see cref="HandleChunk"/> once per arriving chunk.
/// After the chunk with <c>isLastChunk=true</c> the dispatcher
/// completes the request and discards the result — results are
/// single-use.
/// </para>
/// </remarks>
internal abstract class TcrChunkedResult
{
    /// <summary>
    /// Process one chunk of a chunked reply. Called in arrival order;
    /// implementations accumulate state across calls. Mirrors cppcache
    /// <c>TcrChunkedResult::handleChunk</c>.
    /// </summary>
    /// <param name="payload">The chunk's payload bytes (the body that
    /// follows the per-chunk header — payload length already consumed
    /// by the dispatcher). The memory is owned by the dispatcher; do
    /// not retain a reference past the call return.</param>
    /// <param name="isLastChunk">True on the final chunk (cppcache
    /// <c>lastChunkBit</c>). The dispatcher considers the reply
    /// complete after this call returns.</param>
    public abstract void HandleChunk(ReadOnlyMemory<byte> payload, bool isLastChunk);

    /// <summary>
    /// Drop any partial state accumulated by prior
    /// <see cref="HandleChunk"/> calls. Called by the dispatcher
    /// before the first chunk of a (re)attempt — e.g., after failing
    /// over to a different endpoint. Mirrors cppcache
    /// <c>TcrChunkedResult::reset</c>.
    /// </summary>
    public abstract void Reset();
}

*/