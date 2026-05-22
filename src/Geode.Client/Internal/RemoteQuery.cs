using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Concrete <see cref="IQuery{T}"/>. Mirrors cppcache <c>RemoteQuery</c>
/// (<c>cppcache/src/RemoteQuery.hpp/.cpp</c>), reduced to the Phase 1.4
/// surface ??<c>compile()</c> / <c>isCompiled()</c> were never
/// supported upstream and are omitted; the multi-user
/// <c>AuthenticatedView</c> field reappears in Phase 3.
/// </summary>
/// <remarks>
/// Created by <see cref="RemoteQueryService.NewQuery{T}"/>. Not
/// thread-safe per cppcache contract; use one instance per
/// thread / scope.
/// </remarks>
internal sealed class RemoteQuery<T>(
    string oql,
    RemoteQueryService queryService,
    ThinClientBaseDM dm,
    TcrMessageBuilder messageBuilder,
    EventIdGenerator eventIdGenerator,
    IServiceProvider serviceProvider,
    ILogger<RemoteQuery<T>> logger) : IQuery<T>
{

    /// <inheritdoc />
    public string QueryString { get; } = oql;

    /// <inheritdoc />
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public IList<object?> Parameters { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default)
        => ExecuteCoreAsync(ct);

    // ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�
    //  Shared execution path. Mirrors cppcache RemoteQuery::execute
    //  + executeNoThrow merged (RemoteQuery.cpp:67-182). Both public
    //  ExecuteAsync overloads delegate here.
    //
    //  ?�?� Pre-requisite work ?�?�
    //   A1. TcrMessageBuilder.Query                    ??done
    //   A2. TcrMessageBuilder.QueryWithParameters      ??done
    //   A3. ChunkedQueryResponse<T> (TcrChunkedResult) ??pending
    //
    //  ?�?� Phase 1.4 skipped (cppcache surface we omit) ?�?�
    //   ??GuardUserAttributes / AuthenticatedView binding (Phase 3)
    //   ??pool->getStats().incQueryExecutionId() (Phase 1.5 stats)
    //   ??enableTimeStatistics / sampleStartNanos (Phase 1.5 stats)
    //   ??PROTOCOL_OPERATION_TIMEOUT_BOUNDS validation
    //   ??compile() / isCompiled() ??cppcache itself throws unsupported
    //
    private async Task<IReadOnlyList<T>> ExecuteCoreAsync(CancellationToken ct)
    {
        // B1 ??Closed guard. cppcache RemoteQuery.cpp:127-130:
        //   shared_lock(m_queryService->getMutex());
        //   if (m_queryService->invalid()) return GF_CACHE_CLOSED_EXCEPTION;
        // cppcache's shared_lock against destroy is not ported ??the
        // race window is benign (B6's wire send fails naturally if
        // the pool's connections are gone). See RemoteQueryService.IsClosed.
        ObjectDisposedException.ThrowIf(queryService.IsClosed, queryService);

        // B2 ??Log "executing query". cppcache RemoteQuery.cpp:125
        //   LOGFINEST("%s: executing query: %s", func, m_queryString)
        // ("func" is the cppcache call-site label, always
        // "Query::execute" for this path ??kept verbatim so
        // side-by-side cppcache trace comparisons line up.)
        logger.LogTrace("Query::execute: executing query: {Oql}", QueryString);

        // B3 ??Build TcrMessage request. cppcache RemoteQuery.cpp:132-139
        // (Query(34)) / 158-162 (QueryWithParameters(80)). ResponseTimeout
        // ??ms (cppcache m_messageResponseTimeout). Wire branch decided
        // by Parameters.Count: empty ??Query(34), non-empty ??        // QueryWithParameters(80).
        var timeoutMs = (int)ResponseTimeout.TotalMilliseconds;
        TcrMessage request;
        if (Parameters.Count == 0)
        {
            // Query(34) needs an EventId; cppcache TcrMessageQuery emits
            // writeEventIdPart unconditionally. Reuse the per-cache
            // EventIdGenerator that Put / ClearRegion already drive.
            var (threadId, sequenceId) = eventIdGenerator.Next();
            request = await messageBuilder.QueryAsync(
                QueryString,
                eventThreadId: threadId,
                eventSequenceId: sequenceId,
                messageResponseTimeoutMillis: timeoutMs,
                ct: ct);
        }
        else
        {
            // QueryWithParameters(80) omits the EventId part (cppcache
            // TcrMessageQueryWithParameters ctor doesn't call
            // writeEventIdPart).
            request = await messageBuilder.QueryWithParametersAsync(
                QueryString,
                Parameters,
                messageResponseTimeoutMillis: timeoutMs,
                ct: ct);
        }

        // B4 ??Build ChunkedQueryResponse<T> collector (A3). cppcache
        // RemoteQuery.cpp:84-87 ??std::unique_ptr<ChunkedQueryResponse>
        // bound to reply via setChunkedResultHandler. Our chunked DM
        // overload takes the collector directly in B6; nothing to bind
        // here, just construct. ActivatorUtilities mirrors what
        // ThinClientRegion does for its Chunked*Response collectors.
        var collector =
            ActivatorUtilities.CreateInstance<ChunkedQueryResponse<T>>(serviceProvider);

        // B5 ??Log "sending request". cppcache RemoteQuery.cpp:143
        // (Query branch) / :166 (QueryWithParameters branch) ??same
        // LOGFINEST text in both paths.
        logger.LogTrace("Query::execute: sending request for query: {Oql}", QueryString);

        // B6 ??Wire. cppcache RemoteQuery.cpp:147 (Query branch) / :170
        // (QueryWithParameters branch): err = tcdm->sendSyncRequest(msg, reply).
        // Connection error surfaces as IOException / GeodeException
        // (.NET exceptions replace cppcache GfErrType).
        var reply = await dm
            .SendSyncRequestAsync(request, collector, ct: ct)
            .ConfigureAwait(false);

        // B7 ??Server-exception handling. cppcache RemoteQuery.cpp:151-156
        // (Query) / :174-179 (QueryWithParameters). cppcache only
        // special-cases EXCEPTION here; any other reply type falls
        // through to read collector results. We mirror that ??strict
        // "unexpected MessageType" guard can land if integration tests
        // surface a server quirk worth catching.
        if (reply.MessageType == MessageType.Exception)
        {
            throw new GeodeException(
                $"Server exception on Query '{QueryString}': " +
                TcrMessageHelper.DecodeExceptionPreview(reply));
        }

        // B8 ??Log "reading reply". cppcache RemoteQuery.cpp:93.
        logger.LogTrace("Query::execute: reading reply for query: {Oql}", QueryString);

        // B9 / B10 ??Read collector.Results directly. The collector
        // stores already-typed rows (List<T?>): single-column queries
        // push cast row values, multi-column projection pushes
        // assembled QueryStruct per row. cppcache's RemoteQuery.cpp:94-111
        // does the ResultSetImpl / StructSetImpl wrapping at this site;
        // we collapse it into the collector so this leg is one line.
        //
        // B11 ??Log "creating result set". cppcache RemoteQuery.cpp:98 / :107.
        logger.LogTrace("Query::execute: creating result set for query: {Oql}", QueryString);

        // collector.Results is IReadOnlyList<T?>; ExecuteAsync returns
        // IReadOnlyList<T>. Same runtime type for unconstrained T; the
        // `!` suppresses the nullability annotation gap (caller takes
        // null elements as they come ??server may send NULL row values).
        return collector.Results!;
    }

}
