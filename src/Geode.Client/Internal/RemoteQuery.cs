namespace Geode.Client.Internal;

/// <summary>
/// Concrete <see cref="IQuery{T}"/>. Mirrors cppcache <c>RemoteQuery</c>
/// (<c>cppcache/src/RemoteQuery.hpp/.cpp</c>), reduced to the Phase 1.4
/// surface — <c>compile()</c> / <c>isCompiled()</c> were never
/// supported upstream and are omitted; the multi-user
/// <c>AuthenticatedView</c> field reappears in Phase 3.
/// </summary>
/// <remarks>
/// Created by <see cref="RemoteQueryService.NewQuery{T}"/>. Not
/// thread-safe per cppcache contract; use one instance per
/// thread / scope.
/// </remarks>
internal sealed class RemoteQuery<T> : IQuery<T>
{
    // cppcache RemoteQuery.hpp:44 — m_queryService. Held for lifetime
    // anchoring + cppcache symmetry; .NET GC doesn't require it but
    // mirroring keeps porting straightforward.
#pragma warning disable CS0414 // unused while ExecuteAsync is a stub
    private readonly RemoteQueryService _queryService;
    private readonly ThinClientBaseDM _dm;            // cppcache m_tccdm
#pragma warning restore CS0414

    public RemoteQuery(string oql, RemoteQueryService queryService, ThinClientBaseDM dm)
    {
        QueryString = oql;
        _queryService = queryService;
        _dm = dm;
    }

    /// <inheritdoc />
    public string QueryString { get; }

    /// <inheritdoc />
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default)
        => ExecuteCoreAsync(parameters: null, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> ExecuteAsync(
        IReadOnlyList<object?> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return ExecuteCoreAsync(parameters, ct);
    }

    // ────────────────────────────────────────────────────────────
    //  Shared execution path. Mirrors cppcache RemoteQuery::execute
    //  (timeout, func, tcdm, paramList) + executeNoThrow merged
    //  (RemoteQuery.cpp:67-182). Both public overloads delegate here.
    //
    //  ── Pre-requisite work (not yet in codebase) ──
    //
    //   A1. TcrMessage.BuildQuery(oql)
    //       — cppcache TcrMessageQuery ctor.
    //       — wire: MessageType.Query(34) + 1 part (oql string).
    //
    //   A2. TcrMessage.BuildQueryWithParameters(oql, parameters)
    //       — cppcache TcrMessageQueryWithParameters ctor.
    //       — wire: MessageType.QueryWithParameters(82) + parts for
    //         query string, param count, timeout, each serialised param.
    //
    //   A3. ChunkedQueryResponse<T> (new TcrChunkedResult subclass)
    //       — cppcache ChunkedQueryResponse.
    //       — per-chunk decoder of row values; exposes Results (List<T>)
    //         and StructFieldNames (Phase 1.4 always empty); uses
    //         TypedResultAdapter for object→T conversion.
    //
    //  ── Step list (mirrors cppcache numbered comments) ──
    //
    //   B1. Closed guard. cppcache RemoteQuery.cpp:127-130:
    //         shared_lock(m_queryService->getMutex());
    //         if (m_queryService->invalid()) return GF_CACHE_CLOSED_EXCEPTION;
    //       → ObjectDisposedException.ThrowIf on _queryService._invalid.
    //
    //   B2. Log "executing query". cppcache RemoteQuery.cpp:125 LOGFINEST
    //       → _logger.LogTrace("Executing query: {Oql}", QueryString).
    //
    //   B3. Build TcrMessage request. cppcache 132-139 / 158-162:
    //         parameters is null  → A1 BuildQuery(QueryString)
    //         parameters non-null → A2 BuildQueryWithParameters(...)
    //       Sets msg.Timeout — Phase 1.4 deferred (we rely on ct;
    //       timeout option lands with PoolOptions in Phase 1.5).
    //
    //   B4. Build ChunkedQueryResponse<T> collector (A3).
    //
    //   B5. Log "sending request". cppcache 143/166 LOGFINEST
    //       → _logger.LogTrace("Sending request: {Oql}", QueryString).
    //
    //   B6. Wire. cppcache 147/170:
    //         err = tcdm->sendSyncRequest(msg, reply);
    //       → reply = await _dm.SendSyncRequestAsync(request, collector,
    //                                                ct: ct);
    //       Connection error surfaces as IOException / GeodeException
    //       (.NET exceptions replace cppcache GfErrType).
    //
    //   B7. Server-exception handling. cppcache 151-156 / 174-179:
    //         if (reply.getMessageType() == EXCEPTION) {
    //           err = ThinClientRegion::handleServerException(...);
    //           if (err == GF_CACHESERVER_EXCEPTION)
    //             err = GF_REMOTE_QUERY_EXCEPTION;
    //         }
    //       → if (reply.MessageType == MessageType.Exception)
    //             throw new GeodeException(reply.ExceptionMessage);
    //
    //   B8. Log "reading reply". cppcache 93 LOGFINEST.
    //
    //   B9. Read collector.Results + collector.StructFieldNames.
    //
    //   B10. ResultSet vs StructSet branch. cppcache 97-111:
    //          fieldNameVec.size() == 0 → ResultSetImpl(values)
    //          else                     → StructSetImpl(values, names)
    //        Phase 1.4: StructFieldNames is always empty (SELECT *
    //        single-column / SELECT COUNT(*)) → always treat as
    //        ResultSet, return collector.Results directly.
    //        StructSet branch is Phase 2 (multi-column projection).
    //
    //   B11. Log "creating ResultSet" — cppcache 98 LOGFINEST.
    //
    //  ── Phase 1.4 skipped (cppcache RemoteQuery.cpp surface we omit) ──
    //
    //   • GuardUserAttributes / AuthenticatedView binding (Phase 3)
    //   • pool->getStats().incQueryExecutionId() (Phase 1.5 stats)
    //   • enableTimeStatistics / sampleStartNanos (Phase 1.5 stats)
    //   • PROTOCOL_OPERATION_TIMEOUT_BOUNDS validation (Phase 1.5 timeout)
    //   • compile() / isCompiled() — cppcache itself throws unsupported
    //
    private Task<IReadOnlyList<T>> ExecuteCoreAsync(
        IReadOnlyList<object?>? parameters,
        CancellationToken ct)
    {
        _ = parameters; _ = ct;     // suppress unused-warning until B1-B11 land.
        throw new NotImplementedException(
            "Phase 1.4 — pre-requisites A1 / A2 / A3 not yet built.");
    }
}
