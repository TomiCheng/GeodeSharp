using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-scoped (or, eventually, cache-scoped) implementation of
/// <see cref="IQueryService"/>. Mirrors cppcache
/// <c>RemoteQueryService</c>
/// (<c>cppcache/src/RemoteQueryService.hpp/.cpp</c>), reduced to the
/// Phase 1.4 surface (no CQ).
/// </summary>
/// <remarks>
/// <para>
/// Owned by <see cref="ThinClientPoolDM"/>, one instance per pool —
/// matches cppcache's pool-mode ctor path
/// (<c>m_tccdm = poolDM</c>). The non-pool path
/// (<c>m_tccdm = new ThinClientCacheDistributionManager(...)</c>) is
/// deferred per memory <c>pool-only-no-non-pool.md</c>; the
/// <see cref="ThinClientBaseDM"/> ctor parameter keeps that door open
/// without forcing it.
/// </para>
/// <para>
/// Construction is cheap (no I/O). cppcache <c>init()</c> only does
/// work when CQ is enabled — Phase 1.4 omits the init entry point
/// entirely; it reappears with CQ in Phase 2.
/// </para>
/// </remarks>
internal sealed class RemoteQueryService : IQueryService
{
    private readonly ThinClientBaseDM _dm;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RemoteQueryService> _logger;

    public RemoteQueryService(
        ThinClientBaseDM dm,
        IServiceProvider serviceProvider,
        ILogger<RemoteQueryService> logger)
    {
        _dm = dm;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // cppcache RemoteQueryService.cpp:46 — LOGFINEST("Initialized m_tccdm").
        _logger.LogTrace("Initialized m_tccdm");
    }

    /// <summary>
    /// Mirrors cppcache <c>m_invalid</c>: 0 = live, 1 = closed.
    /// cppcache ctor sets it true and <c>init()</c> flips it false;
    /// Phase 1.4 has no init step (pool-mode RQS init is a no-op
    /// besides the flag, CQ-only work lives in Phase 2), so we start
    /// at 0 directly. <see cref="Close"/> flips to 1.
    /// </summary>
    private int _invalid;

    public IQuery<T> NewQuery<T>(string oql)
    {
        // step 1 — input validation. cppcache does not; server's OQL
        // parser catches empty / whitespace. We fail fast client-side.
        ArgumentException.ThrowIfNullOrWhiteSpace(oql);

        // step 3 — closed guard. Mirrors cppcache
        // RemoteQueryService::newQuery's `if (m_invalid) throw
        // CacheClosedException(...)`. ObjectDisposedException is the
        // .NET-side parallel (memory use-bcl-exceptions.md: BCL for
        // lifecycle misuse, GeodeException for protocol failures).
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _invalid) != 0, this);

        // step 4 — diagnostics. Mirrors cppcache
        // RemoteQueryService.cpp:69-71:
        //   LOGDEBUG("...newQuery: multiuserMode = %d", ...)
        //   LOGDEBUG("RemoteQueryService: creating a new query: " + qs)
        // multiuserMode is always false in Phase 1.4 (Phase 3 will
        // surface ThinClientBaseDM.IsMultiUserMode); the log line is
        // kept so cppcache trace comparisons line up.
        _logger.LogDebug(
            "RemoteQueryService::newQuery: multiuserMode = {MultiUser}",
            _dm.IsMultiUserMode);
        _logger.LogDebug(
            "RemoteQueryService: creating a new query: {Oql}", oql);

        // step 5 — multi-user branch. Mirrors cppcache
        // RemoteQueryService.cpp:73-80. Phase 1.4 _dm.IsMultiUserMode
        // is hard-wired false (ThinClientBaseDM default), so this
        // branch is structurally unreachable today; it stays here so
        // Phase 3 only has to fill in the AuthenticatedView bind and
        // delete the NIE.
        if (_dm.IsMultiUserMode)
        {
            // cppcache:
            //   return std::make_shared<RemoteQuery>(
            //       querystring, shared_from_this(), m_tccdm,
            //       UserAttributes::threadLocalUserAttributes->getAuthenticatedView());
            throw new NotImplementedException(
                "Multi-user authentication mode is Phase 3 scope.");
        }

        // step 6/7 — single-user build + return. Mirrors cppcache
        // RemoteQueryService.cpp:78-79:
        //   return std::make_shared<RemoteQuery>(querystring,
        //                                        shared_from_this(), m_tccdm);
        // Built via ActivatorUtilities so future DI-resolved deps
        // (logger, TypedResultAdapter, SerializationRegistry) flow in
        // automatically — oql / this / _dm supply the non-DI args.
        return ActivatorUtilities.CreateInstance<RemoteQuery<T>>(
            _serviceProvider, oql, this, _dm);
    }

    /// <summary>
    /// Mark this service closed; subsequent <see cref="NewQuery{T}"/>
    /// calls throw <see cref="ObjectDisposedException"/>. Mirrors
    /// cppcache <c>RemoteQueryService::close()</c> reduced to the
    /// Phase 1.4 surface — CQ service teardown
    /// (<c>m_cqService-&gt;closeCqService()</c>) and non-pool DM
    /// destroy reappear in Phase 2 / when non-pool mode ships.
    /// Idempotent.
    /// </summary>
    internal void Close()
    {
        // cppcache RemoteQueryService.cpp:84 — LOGFINEST("...close: starting close").
        _logger.LogTrace("RemoteQueryService::close: starting close");

        Interlocked.Exchange(ref _invalid, 1);

        // cppcache RemoteQueryService.cpp:107 — LOGFINEST("...close: completed").
        _logger.LogTrace("RemoteQueryService::close: completed");
    }
}
