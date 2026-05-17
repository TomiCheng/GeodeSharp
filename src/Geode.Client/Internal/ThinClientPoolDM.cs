using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-mode distribution manager. Mirrors cppcache
/// <c>ThinClientPoolDM</c>
/// (<c>cppcache/src/ThinClientPoolDM.hpp/.cpp</c>) &#x2014; the heart
/// of pool-mode operation: connection registry, three background
/// workers, retry / failover, single-hop routing.
/// </summary>
/// <remarks>
/// <para>
/// cppcache multi-inheritance
/// (<c>ThinClientBaseDM + Pool + ConnectionQueue</c>) is flattened
/// per CLAUDE.md "Three-bucket rule": this class
/// <see cref="ThinClientBaseDM">inherits the base DM</see>,
/// <see cref="IPool">implements the pool interface</see>, and
/// holds its connection queue via composition.
/// </para>
/// <para>
/// MVP (Phase 1.1) needs only a tiny slice: open one
/// <see cref="TcrEndpoint"/> + one <see cref="TcrConnection"/> in
/// <see cref="InitAsync"/>, close it in <see cref="DestroyAsync"/>.
/// The full machinery (locator helper, three background workers,
/// connection queue, single-hop metadata, HA subscription, sticky
/// transactions) is Phase 1.5 / 2+ / 4 / 6 respectively.
/// </para>
/// </remarks>

internal sealed class ThinClientPoolDM(
    CachePoolOptions xmlPool,
    GeodeClientOptions options,
    TcrConnectionManager connManager,
    IServiceProvider serviceProvider,
    ILogger<ThinClientPoolDM> logger) : ThinClientBaseDM(connManager, region: null), IPool
{

    // ── Background workers (Phase 1.5, pool-mode equivalents of TCCM trio) ──

    private readonly CancellationTokenSource _backgroundCts = new();
    // ── Endpoint registry (ThinClientPoolDM.hpp m_endpoints) ──
    // Pool's view onto TCCM-owned TcrEndpoint instances. Same object
    // identity as TcrConnectionManager._endpoints; this map tracks
    // which endpoints THIS pool currently holds a ref on so destroy
    // knows what to release. Key uses DnsEndPoint default equality.
    private readonly ConcurrentDictionary<DnsEndPoint, TcrEndpoint> _endpoints = new();
    private readonly Lock _endpointSelectionLock = new();             // m_endpointSelectionLock
    /// <summary>
    /// 0 = <see cref="InitAsync"/> not run, 1 = ran. Mirrors cppcache
    /// pool DM's one-shot init guard; gated by
    /// <see cref="Interlocked.Exchange(ref int, int)"/>.
    /// </summary>
    private int _initGuard;
    private int _isDestroyed;                      // m_isDestroyed (Interlocked 0/1)
    private bool _keepAlive;                       // m_keepAlive (set in DestroyAsync, read by Step 5a)
    // ── Idle connection queue (cppcache inherits ConnectionQueue<TcrConnection>) ──
    // Unbounded for Phase 1.1; Phase 1.5 may bound by MaxConnections.
    // Channel auto-wakes a pending reader on WriteAsync — replaces
    // cppcache's conn_semaphore_.release().
    private readonly Channel<TcrConnection> _opConnections = Channel.CreateUnbounded<TcrConnection>();
    private int _poolSize;

    // MaxConnections cap enforcement. SemaphoreSlim acts as a "slot
    // reservation" — Wait(0) at open time, Release on close. null means
    // unbounded (MaxConnections not set). cppcache parity: serialises
    // cap check + reservation atomically, fixing the race that the
    // earlier Volatile.Read + Interlocked.Increment pair had.
    // Future FreeConnectionTimeout (CachePoolOptions.FreeConnectionTimeout)
    // wires by switching Wait(0) → WaitAsync(timeout, ct).
    private readonly SemaphoreSlim? _capSlots = xmlPool.MaxConnections is int cap
        ? new SemaphoreSlim(cap, cap)
        : null;

    /// <summary>
    /// Pool-scoped query service. Mirrors cppcache
    /// <c>ThinClientPoolDM::m_remoteQueryService</c> — eagerly tied to
    /// this pool (cppcache builds it in the pool ctor). Lazy here only
    /// because primary-ctor field initialisers can't reference
    /// <c>this</c>; the creation itself is zero-I/O. Built via
    /// <see cref="ActivatorUtilities"/> so DI-resolved dependencies
    /// (logger, serialization registry, future stats) flow in
    /// automatically — <see langword="this"/> supplies the
    /// <see cref="ThinClientBaseDM"/> argument.
    /// </summary>
    private RemoteQueryService? _queryService;

    // ── Static-server round-robin cursor (ThinClientPoolDM.cpp:608) ──
    // Guarded by _endpointSelectionLock; mirrors cppcache m_server +
    // m_endpointSelectionLock. SelectEndpointAsync reads + post-increments
    // (with wrap) under the lock.
    private int _server;                                              // m_server

    // ── Stats (Phase 1.5 thin wrapper around Meter) ──
    private readonly PoolStatistics _stats = ActivatorUtilities.CreateInstance<PoolStatistics>(serviceProvider, xmlPool.Name);

    /// <summary>
    /// Get-or-create the pool's view of <paramref name="endpointName"/>'s
    /// <see cref="TcrEndpoint"/>, taking a TCCM-level reference on
    /// first sight. Mirrors cppcache
    /// <c>ThinClientPoolDM::addEP(string)</c>.
    /// </summary>
    /// <remarks>
    /// Per-pool dedupe: each pool only takes one TCCM ref per unique
    /// <c>"host:port"</c>, even when
    /// <see cref="CreatePoolConnectionAsync"/> is called many times for
    /// the same endpoint (the normal case once
    /// <c>MinConnections &gt; 1</c> or after Phase 1.2's request path
    /// drives queue starvation). Phase 1.5 may tighten the dedupe race
    /// (two concurrent first-sight callers) with
    /// <see cref="Lazy{T}"/>; Phase 1.1 has only the serial
    /// conn-management loop, so a missed dedupe is presently
    /// unreachable.
    /// </remarks>
    private async Task<TcrEndpoint> AddEPAsync(DnsEndPoint endpointAddress, CancellationToken ct)
    {
        if (_endpoints.TryGetValue(endpointAddress, out var cached))
        {
            return cached;
        }

        var endpoint = await ConnManager
            .AddRefToTcrEndpointAsync(endpointAddress, this, ct)
            .ConfigureAwait(false);
        _endpoints.TryAdd(endpointAddress, endpoint);
        return endpoint;
    }

    /// <summary>
    /// Open exactly one new <see cref="TcrConnection"/>. Mirrors
    /// cppcache <c>ThinClientPoolDM::createPoolConnection()</c>:
    /// select an endpoint (locator or static server list), get-or-
    /// create its <see cref="TcrEndpoint"/> from the registry,
    /// open the connection on it, enqueue. Returns <c>null</c> when
    /// no endpoint can currently be reached.
    /// </summary>
    private async Task<TcrConnection?> CreatePoolConnectionAsync(
        HashSet<DnsEndPoint> excludeServers,
        TcrConnection? currentServer = null,
        CancellationToken ct = default)
    {
        // Failover retry loop mirroring cppcache createPoolConnection
        // (ThinClientPoolDM.cpp:1725-1802). MaxConnections cap enforced
        // via _capSlots semaphore: Wait(0) reserves a slot atomically
        // up-front; finally releases unless ownership transferred to a
        // freshly-opened conn (success path clears releaseSlot).
        if (_capSlots is not null && !_capSlots.Wait(0, ct))
        {
            throw new AllConnectionsInUseException(
                $"Pool '{xmlPool.Name}': MaxConnections={xmlPool.MaxConnections} reached.");
        }

        var releaseSlot = true;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                DnsEndPoint location;
                try
                {
                    location = await SelectEndpointAsync(excludeServers, ct).ConfigureAwait(false);
                }
                catch (GeodeException ex) when (ex is not NoAvailableLocatorsException)
                {
                    // cppcache L1752-1755: generic endpoint-selection fail
                    // (e.g. NotConnectedException when every static server
                    // is excluded) → return null, caller bails. Slot
                    // released by the outer finally.
                    logger.LogDebug(ex, "Endpoint selection exhausted; bailing");
                    return null;
                }
                // NoAvailableLocators is fatal-client per cppcache
                // isFatalClientError (L1749-1751); propagates through the
                // outer finally so the slot release still fires.

                logger.LogDebug("Connecting to {Host}:{Port}", location.Host, location.Port);
                var endpoint = await AddEPAsync(location, ct).ConfigureAwait(false);

                // cppcache L1760-1765 — currentServer recycle: when SelectEndpoint
                // picked the same endpoint the dying conn lives on, don't waste a
                // handshake. Reset the load-conditioning clock on the old conn
                // and return it. Pool size unchanged (slot stays with currentServer).
                if (currentServer is not null && ReferenceEquals(currentServer.Endpoint, endpoint))
                {
                    logger.LogDebug("Recycling existing connection to {Endpoint}", endpoint.Name);
                    currentServer.UpdateCreationTime();
                    return currentServer;
                }

                TcrConnection conn;
                try
                {
                    conn = await endpoint.CreateNewConnectionAsync(false, false, options.Pool.ConnectTimeout, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is AuthenticationFailedException
                                              or AuthenticationRequiredException
                                              or NotAuthorizedException
                                              or NoAvailableLocatorsException)
                {
                    // cppcache isFatalClientError (L1787-1791): the same failure
                    // will hit every server in the cluster (auth realm shared,
                    // locator dead). Propagate; slot released by outer finally.
                    throw;
                }
                catch (Exception ex)
                {
                    // cppcache isFatalError ∪ transient (L1772-1786): blacklist
                    // this server and try the next. Covers SocketException,
                    // IOException, TimeoutException, NotConnectedException, plus
                    // CacheServerException ("fatal-but-keep-trying-next" — the
                    // next server might be healthy). Slot stays reserved — same
                    // reservation carries to the next iteration.
                    logger.LogDebug(ex, "Failed to open conn to {Endpoint}, retrying with next", endpoint.Name);
                    excludeServers.Add(location);
                    continue;
                }

                endpoint.SetConnected(true);
                var newSize = Interlocked.Increment(ref _poolSize);
                _stats.PoolConnect();
                // cppcache :1707-1711 — pool growing past Min means this conn is
                // "extra" load-conditioning capacity rather than warm-up.
                if (newSize > xmlPool.MinConnections)
                {
                    _stats.LoadConditioningConnect();
                }

                // Slot ownership transfers to the freshly-opened conn; the
                // matching Release will fire on its close.
                releaseSlot = false;
                return conn;
            }
        }
        finally
        {
            if (releaseSlot) _capSlots?.Release();
        }
    }

    /// <summary>
    /// Open a fresh <see cref="TcrConnection"/> on a specific
    /// <paramref name="endpoint"/>, bypassing
    /// <see cref="SelectEndpointAsync"/>. Mirrors cppcache
    /// <c>ThinClientPoolDM::createPoolConnectionToAEndPoint</c>
    /// (<c>ThinClientPoolDM.cpp:1663-1718</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Caller must have already registered <paramref name="endpoint"/>
    /// via <see cref="AddEPAsync"/> (or be iterating
    /// <see cref="_endpoints"/> directly, as
    /// <see cref="PingServerLocalAsync"/> does). cppcache makes the same
    /// assumption — this helper does not AddEP.
    /// </para>
    /// <para>
    /// Returns <c>null</c> when the endpoint cannot currently be reached;
    /// the caller (<see cref="SendRequestToEndpointAsync"/>) then falls
    /// back to its own error path. Unlike
    /// <see cref="CreatePoolConnectionAsync"/> this does NOT enqueue —
    /// the caller uses the conn immediately and returns it to the queue
    /// after the send.
    /// </para>
    /// </remarks>
    private async Task<TcrConnection?> CreatePoolConnectionToAEndPointAsync(
        TcrEndpoint endpoint, CancellationToken ct)
    {
        // MaxConnections cap (cppcache ThinClientPoolDM.cpp:1672-1687) —
        // same SemaphoreSlim pattern as CreatePoolConnectionAsync. cppcache
        // signals "cap reached" via a maxConnLimit out-flag so the caller
        // can fall back to a temporary non-pool conn; we throw
        // AllConnectionsInUseException and let the caller catch it.
        if (_capSlots is not null && !_capSlots.Wait(0, ct))
        {
            throw new AllConnectionsInUseException(
                $"Pool '{xmlPool.Name}': MaxConnections={xmlPool.MaxConnections} reached.");
        }

        var releaseSlot = true;
        try
        {
            logger.LogDebug("ThinClientPoolDM::createPoolConnectionToAEndPoint: opening new connection to {Endpoint}",
                endpoint.Name);

            TcrConnection conn;
            try
            {
                conn = await endpoint
                    .CreateNewConnectionAsync(
                        isClientNotification: false,
                        isSecondary: false,
                        connectTimeout: options.Pool.ConnectTimeout,
                        ct: ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ThinClientPoolDM::createPoolConnectionToAEndPoint: failed to connect to {Endpoint}",
                    endpoint.Name);
                return null;
            }

            // cppcache L1704-1712: mark endpoint healthy + grow counter + stats.
            endpoint.SetConnected(true);
            var newSize = Interlocked.Increment(ref _poolSize);
            _stats.PoolConnect();
            if (newSize > xmlPool.MinConnections)
            {
                _stats.LoadConditioningConnect();
            }

            // Slot ownership transfers to the freshly-opened conn.
            releaseSlot = false;
            return conn;
        }
        finally
        {
            if (releaseSlot) _capSlots?.Release();
        }
    }

    /// <summary>
    /// Try borrow an idle <see cref="TcrConnection"/> already attached to
    /// <paramref name="endpoint"/>. Mirrors cppcache
    /// <c>ThinClientPoolDM::getFromEP</c>.
    /// </summary>
    /// <returns>An idle conn for this endpoint, or <c>null</c> if none available.</returns>
    private Task<TcrConnection?> GetFromEPAsync(TcrEndpoint endpoint, CancellationToken ct)
    {
        // TODO Phase 1.5 (multi-endpoint): scan _opConnections for a conn
        //   whose endpoint == endpoint; cppcache walks its queue and
        //   filters by getEndpointObject(). Requires TcrConnection to
        //   carry a back-ref to its TcrEndpoint (cppcache m_endpointObj).
        // Phase 1.1 single-endpoint shortcut: any conn in _opConnections
        //   belongs to the only endpoint, so TryRead is sufficient.
        _ = endpoint;
        _ = ct;
        return _opConnections.Reader.TryRead(out var conn)
            ? Task.FromResult<TcrConnection?>(conn)
            : Task.FromResult<TcrConnection?>(null);
    }

    /// <summary>
    /// Return a borrowed <see cref="TcrConnection"/> to the pool queue.
    /// Mirrors cppcache <c>ThinClientPoolDM::put(conn, isTransaction)</c>
    /// (the <c>false</c> overload — sticky-tx routing is Phase 6).
    /// </summary>
    private ValueTask PutInQueueAsync(TcrConnection conn, CancellationToken ct)
    {
        // Stamp last-access before queueing so CleanStaleConnectionsAsync
        // can age out idle conns. Phase 6: route to sticky-tx queue when
        // forTransaction=true.
        conn.Touch();
        return _opConnections.Writer.WriteAsync(conn, ct);
    }

    /// <summary>
    /// Open new <see cref="TcrConnection"/>s until the pool holds at
    /// least <c>MinConnections</c>. Mirrors cppcache
    /// <c>ThinClientPoolDM::restoreMinConnections()</c>.
    /// </summary>
    /// <remarks>
    /// Called by the conn-management loop scheduled in
    /// <see cref="StartBackgroundThreads"/> (Phase 1.5), and
    /// indirectly via the request path when
    /// <see cref="SendSyncRequestAsync"/>'s queue dequeue starves
    /// (Phase 1.2). Each iteration delegates to
    /// <see cref="CreatePoolConnectionAsync"/>; that helper does the
    /// endpoint selection + handshake.
    /// </remarks>
    private async Task RestoreMinConnectionsAsync(CancellationToken ct)
    {
        var min = xmlPool.MinConnections;
        while (Volatile.Read(ref _poolSize) < min)
        {
            ct.ThrowIfCancellationRequested();
            // Fresh blacklist per warm-up attempt: the failover retry
            // within one open is short-lived, no need to carry state across.
            var conn = await CreatePoolConnectionAsync(
                [], currentServer: null, ct).ConfigureAwait(false);
            if (conn is null)
            {
                // No endpoint reachable this cycle — bail; the next
                // conn-management tick will retry. Avoids spinning
                // when every endpoint is unhealthy.
                break;
            }

            // Warm-up path enqueues; sendSyncRequest's starvation path
            // (Phase 1.2) will consume the conn directly. Mirrors
            // cppcache restoreMinConnections → putInQueue(conn).
            await _opConnections.Writer.WriteAsync(conn, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pick the next endpoint name (<c>host:port</c>) to open a
    /// connection on. Mirrors cppcache
    /// <c>ThinClientPoolDM::selectEndpoint</c>
    /// (<c>ThinClientPoolDM.cpp:577-632</c>) &#x2014; the locator vs
    /// static-server-list branching point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Priority mirrors cppcache: <c>Locators</c> wins when non-empty,
    /// otherwise fall through to <c>Servers</c>. Phase 1.1 fills the
    /// static-server branch with the cppcache round-robin cursor
    /// (<c>m_server</c> + <c>m_endpointSelectionLock</c>); the locator
    /// branch is NIE and the ctor's <c>Locators.Count &gt; 0</c>
    /// guard rejects locator-config at construction time so
    /// <see cref="Cache"/> init fails fast.
    /// </para>
    /// <para>
    /// Phase 1.5 expansion:
    /// (a) locator branch via
    /// <c>ThinClientLocatorHelper.GetEndpointForNewFwdConnAsync</c>;
    /// (b) <c>ISet&lt;ServerLocation&gt; excludeServers</c> parameter
    /// for <see cref="CreatePoolConnectionAsync"/>'s retry loop &#x2014;
    /// the static-server branch will gain a do-while loop that skips
    /// excluded entries, throwing <c>NotConnectedException</c> once
    /// every server is excluded;
    /// (c) <c>TcrConnection? currentServer</c> parameter for sticky /
    /// refresh paths.
    /// </para>
    /// </remarks>
    private async Task<DnsEndPoint> SelectEndpointAsync(
        HashSet<DnsEndPoint> excludeServers,
        CancellationToken ct = default)
    {
        // Locator branch (priority) — cppcache ThinClientPoolDM.cpp:577-604.
        if (xmlPool.Locators.Count > 0)
        {
            return await SelectEndpointFromLocatorAsync(excludeServers, ct).ConfigureAwait(false);
        }

        // Static server branch — cppcache ThinClientPoolDM.cpp:605-627.
        if (xmlPool.Servers.Count > 0)
        {
            return SelectEndpointFromStaticServerList(excludeServers);
        }

        // Unreachable: AddGeodeClient options validation rejects pools with
        // neither Locators nor Servers. Mirrors cppcache's
        // IllegalStateException("No locators or servers provided").
        throw new InvalidOperationException(
            $"Pool '{xmlPool.Name}' has neither Locators nor Servers configured.");
    }

    /// <summary>
    /// Pick the next entry from the configured <c>Servers</c> list using
    /// the round-robin cursor. Mirrors cppcache
    /// <c>ThinClientPoolDM::selectEndpoint</c> static-server branch
    /// (<c>ThinClientPoolDM.cpp:605-627</c>).
    /// </summary>
    /// <remarks>
    /// Phase 1.5 will turn this into a do-while that skips entries in
    /// <c>excludeServers</c> (cppcache <c>excludeServer</c> helper) and
    /// throws <c>NotConnectedException</c> once every server is excluded.
    /// </remarks>
    private DnsEndPoint SelectEndpointFromStaticServerList(HashSet<DnsEndPoint> excludeServers)
    {
        // Round-robin cursor under lock, walk up to Count entries and pick
        // the first non-excluded. cppcache `selectEndpoint` static branch
        // (ThinClientPoolDM.cpp:605-627) — when every entry is excluded,
        // throw NotConnectedException so the caller's failover loop bails.
        var total = xmlPool.Servers.Count;
        lock (_endpointSelectionLock)
        {
            for (var i = 0; i < total; i++)
            {
                if (_server >= total) _server = 0;
                var position = _server++;
                var server = xmlPool.Servers[position];
                var endpoint = new DnsEndPoint(server.Host, server.Port);
                if (excludeServers.Contains(endpoint)) continue;

                logger.LogDebug(
                    "ThinClientPoolDM: Selecting endpoint [{Host}:{Port}] from position {Position}",
                    endpoint.Host, endpoint.Port, position);
                return endpoint;
            }
        }

        throw new NotConnectedException(
            $"Pool '{xmlPool.Name}': all {total} configured servers are in excludeServers.");
    }

    /// <summary>
    /// Launch the pool's background machinery. Mirrors cppcache
    /// <c>ThinClientPoolDM::startBackgroundThreads()</c>
    /// (<c>ThinClientPoolDM.cpp:264-371</c>). Phase 1.5 fills the
    /// body; Phase 1.1 calls into an empty stub so the InitAsync
    /// flow already has the right shape.
    /// </summary>
    private void StartBackgroundThreads()
    {
        // conn-management loop drives the lazy connection opening
        // (RestoreMinConnectionsAsync). cppcache mirrors:
        //   m_connManageTask = expiryTaskManager.schedule(
        //       manageConnections, 10s initial delay, interval);
        _connManageLoop = ConnManageLoopAsync(_backgroundCts.Token);

        SchedulePingLoop();

        ScheduleUpdateLocatorLoop();

        // TODO Phase 1.5: Statistics sampler — bucket-1 (Meter-based).
        //
        // RemoteQueryService has no init step in Phase 1.4 (cppcache
        // RemoteQueryService::init() only does work when CQ is enabled;
        // pure OQL has nothing to initialise). Reappears with CQ in
        // Phase 2.
    }

    /// <summary>
    /// Test-only: current pool connection count (cppcache <c>m_poolSize</c>).
    /// Bumped in <see cref="CreatePoolConnectionAsync"/> step 4 after a
    /// fresh <see cref="TcrConnection"/> handshakes successfully.
    /// </summary>
    internal int PoolSize => Volatile.Read(ref _poolSize);


    // ── IPool ────────────────────────────────────────────────────

    public override async Task DestroyAsync(bool keepAlive = false, CancellationToken ct = default)
    {
        // Single override satisfies both ThinClientBaseDM.DestroyAsync
        // (virtual) and IPool.DestroyAsync (interface).
        //
        // Mirror cppcache ThinClientPoolDM::destroy() order:
        //   1. mark destroyed (idempotent)
        //   2. cancel background CTS — every loop's Task.Delay /
        //      WaitAsync throws OperationCanceledException
        //   3. await each background Task so they fully unwind
        //   4. dispose timers + sync primitives
        //   5. (TODO Phase 1.1+) drain _opConnections, send
        //      CloseConnection(18) on each, dispose endpoints
        _ = ct;          // current body has no awaits that observe caller's ct;
                         // background cancellation flows through _backgroundCts.

        // 1. Idempotent destroy guard.
        if (Interlocked.Exchange(ref _isDestroyed, 1) != 0)
        {
            return;
        }

        // Stash the caller's keepAlive intent for Step 5a's CloseAsync calls.
        // cppcache: m_keepAlive = keepAlive (ThinClientPoolDM.cpp:789).
        _keepAlive = keepAlive;

        // 1b. Close pool-owned RemoteQueryService if it was ever
        // accessed. Mirrors cppcache CacheImpl::close() →
        // m_remoteQueryServicePtr->close(); we trigger it from pool
        // destroy because the RQS lives on the pool, not the cache.
        // Read the field directly (not the property) — we don't want
        // to lazy-create an RQS just to immediately close it.
        _queryService?.Close();

        // 2. Signal every background loop to stop.
        _backgroundCts.Cancel();

        // 3. Await each loop's graceful exit. OperationCanceledException
        //    is expected here — that IS the graceful exit signal.
        if (_connManageLoop is not null)
        {
            try { await _connManageLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        if (_pingLoop is not null)
        {
            try { await _pingLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        if (_updateLocatorLoop is not null)
        {
            try { await _updateLocatorLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        // 4. Dispose timers + sync primitives owned by this pool.
        _pingTimer?.Dispose();
        _updateLocatorTimer?.Dispose();
        _pingSignal.Dispose();
        _connManageSignal.Dispose();
        _updateLocatorSignal.Dispose();
        _backgroundCts.Dispose();
        _capSlots?.Dispose();

        // 5a. Drain _opConnections — every idle conn gets a polite
        //     CloseConnection(18) before its socket goes away. Mirrors
        //     cppcache ConnectionQueue::close (ConnectionQueue.hpp:87)
        //     invoked from ThinClientPoolDM::destroy (L829).
        _opConnections.Writer.TryComplete();
        while (_opConnections.Reader.TryRead(out var conn))
        {
            // CloseAsync sends MessageType.CloseConnection(18) then
            // disposes the socket. Currently NIE — until the leaf lands,
            // any drained conn here will throw and bubble out of
            // DestroyAsync. Top-down: call site is in place, leaf next.
            await conn.CloseAsync(_keepAlive, ct).ConfigureAwait(false);
        }

        // 5b. TODO Phase 1.5: release pool's TCCM refs to endpoints in
        //   _endpoints (ConnManager.RemoveRefToTcrEndpointAsync). Phase
        //   1.1: rely on cache-scope dispose to cascade.
        _endpoints.Clear();

        // 5c. Unregister the PoolConnections gauge reader so the static
        // registry in PoolStatistics doesn't leak this pool's entry.
        _stats.ClearPoolConnectionsReader();
    }

    // ── Lifecycle (override base + add pool-mode init) ──────────

    public override Task InitAsync(CancellationToken ct = default)
    {
        // ── 1. Pre-check ────────────────────────────────────────
        ct.ThrowIfCancellationRequested();

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, typeof(ThinClientPoolDM));

        // Idempotent: first caller wins. Mirrors cppcache m_initGuard
        // semantics — set BEFORE doing work, no rollback on failure.
        // Concurrent re-entry is prevented by Cache.EnsureInitializedAsync's
        // SemaphoreSlim, so this is purely a "skip if already ran" check.
        if (Interlocked.Exchange(ref _initGuard, 1) != 0)
        {
            return Task.CompletedTask;
        }

        // Register the PoolConnections gauge reader so a listener sees
        // a fresh value from the moment init completes (cppcache pushes
        // via setCurPoolConnections; we pull). Cleared in DestroyAsync.
        _stats.SetPoolConnectionsReader(() => Volatile.Read(ref _poolSize));

        // ── 2. Pool-level flags ─────────────────────────────────
        // cppcache equivalent (ThinClientPoolDM.cpp:217-224):
        //   m_isMultiUserMode = getMultiuserAuthentication();
        //   m_isSecurityOn = cacheImpl->getAuthInitialize() != nullptr;
        // TODO Phase 3 (security):
        //   _isMultiUserMode = _xmlPool.MultiuserAuthentication ?? false;
        //   _isSecurityOn    = _options.Auth?.HasCredentials ?? false;

        // ── 3. TCCM init — deliberately NOT here ────────────────
        // cppcache calls m_connManager.init(true) inside
        // ThinClientPoolDM::init() (ThinClientPoolDM.cpp:228), which
        // means N pools call it N times; the call is idempotent only
        // because cppcache m_initGuard short-circuits the 2nd..Nth.
        // We hoist it up to Cache.InitializeCoreAsync step 2 so it
        // runs exactly once per cache. TCCM is a cache-scoped
        // singleton — re-initialising it from each pool is redundant.
        // End state matches cppcache.

        // ── 4. startBackgroundThreads ───────────────────────────
        StartBackgroundThreads();

        // ── 5. Lazy connection opening ──────────────────────────
        // cppcache deliberately does NOT open any TCP here. First
        // connection opens through one of two paths, both calling
        // selectEndpoint() (ThinClientPoolDM.cpp:577-632) where the
        // locator vs server branching lives:
        //   (a) restoreMinConnections — runs ~10 s after init via the
        //       conn-management Task above; opens up to MinConnections
        //       eagerly in the background.
        //   (b) sendSyncRequest → getConnectionFromQueue →
        //       createPoolConnection → selectEndpoint →
        //       TcrEndpoint.CreateNewConnectionAsync.
        //
        // Phase 1.1 mirrors this: EnsureInitializedAsync completes
        // without any TCP touch. Tests that need to verify the
        // handshake must follow init with a Ping or simple op once
        // sendSyncRequest is wired up (Phase 1.2 / 1.5).

        return Task.CompletedTask;
    }

    /// <summary>
    /// Send <paramref name="request"/> directly to
    /// <paramref name="endpoint"/>, no DM-level routing. Mirrors cppcache
    /// <c>ThinClientPoolDM::sendRequestToEP</c>
    /// (<c>ThinClientPoolDM.cpp:1841-1995</c>) — the path used by
    /// register-interest, subscription, and the ping loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache's body wraps the send in an auth-retry loop (max 2
    /// retries on <c>AuthenticationRequiredException</c>), threads
    /// multi-user creds, classifies server exceptions, and toggles
    /// <c>putConnInPool</c> based on whether a pool conn or temporary
    /// conn was used. Phase 1.1 implements only the bare wire path:
    /// borrow conn → send → return / put-back. Auth retry is Phase 3;
    /// failover branching is Phase 1.5; multi-user is Phase 3.
    /// </para>
    /// </remarks>
    public override async Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrEndpoint endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpoint);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);
       

        logger.LogDebug(
            "ThinClientPoolDM::sendRequestToEP type={MessageType} endpoint={Endpoint}",
            request.MessageType, endpoint.Name);

        // Step 1 — try borrow an idle pool conn for this endpoint.
        //   cppcache: TcrConnection* conn = getFromEP(currentEndpoint);
        var conn = await GetFromEPAsync(endpoint, ct).ConfigureAwait(false);

        // Step 2 — none idle? open a fresh one ON this endpoint.
        //   cppcache: createPoolConnectionToAEndPoint(...) → fallback to
        //   currentEndpoint->createNewConnection (temporary, putConnInPool=false)
        //   if pool-cap reached. Phase 1.1 collapses both branches into one
        //   pool-tracked conn (no maxConn limiter yet).
        var putConnInPool = true;
        conn ??= await CreatePoolConnectionToAEndPointAsync(endpoint, ct).ConfigureAwait(false);

        if (conn is null)
        {
            // cppcache: setConnectionStatus(false) + LOGFINE("3Failed to connect").
            endpoint.SetConnected(false);
            throw new GeodeException(
                $"ThinClientPoolDM: could not obtain a connection to {endpoint.Name}.");
        }

        // TODO Phase 3 — auth / multi-user creds:
        //   if (TcrMessage.IsUserInitiativeOps(request) && (IsSecurityOn || IsMultiUserMode))
        //     await SendUserCredentialsAsync(...);

        try
        {
            // Step 3 — actual wire I/O. cppcache:
            //   currentEndpoint->sendRequestConnWithRetry(request, reply, conn, true)
            // We currently send straight on the conn; the per-conn retry
            // wrap (cppcache's "WithRetry") is Phase 1.5 once timeouts /
            // partial-write recovery surface.
            var reply = await conn.SendRequestAsync(request, ct).ConfigureAwait(false);

            // TODO Phase 3: if reply.MessageType == Exception &&
            //   IsAuthRequireException(reply) → unauth + outer retry loop.

            // Step 4 — happy path: return conn to its endpoint queue.
            //   cppcache: putConnInPool ? put(conn, false) : close+delete(conn).
            if (putConnInPool)
            {
                await PutInQueueAsync(conn, ct).ConfigureAwait(false);
            }
            else
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }

            return reply;
        }
        catch
        {
            // cppcache: setConnectionStatus(false) + removeEPConnections(1)
            // + removeEPFromMetadataIfError. Phase 1.5 will classify the
            // GfErrType and decide whether to truly mark the endpoint
            // down vs. retry on another conn; Phase 1.1 is conservative
            // — any failure on a conn drops it and marks endpoint down.
            endpoint.SetConnected(false);
            if (putConnInPool)
            {
                Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
            }
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Chunked-reply variant of
    /// <see cref="SendRequestToEndpointAsync(TcrMessage, TcrEndpoint, CancellationToken)"/>.
    /// Same conn borrow / put-back shape, only the wire I/O leg differs
    /// (<see cref="TcrConnection.SendRequestAsync(TcrMessage, TcrChunkedResult, CancellationToken)"/>).
    /// </summary>
    public override async Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        TcrEndpoint endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(chunkedResult);
        ArgumentNullException.ThrowIfNull(endpoint);
        ct.ThrowIfCancellationRequested();

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);

        logger.LogDebug(
            "ThinClientPoolDM::sendRequestToEP (chunked) type={MessageType} endpoint={Endpoint}",
            request.MessageType, endpoint.Name);

        // ─── Step 1: try borrow an idle pool conn for this endpoint ──
        //   cppcache: TcrConnection* conn = getFromEP(currentEndpoint);
        var conn = await GetFromEPAsync(endpoint, ct).ConfigureAwait(false);

        // ─── Step 2: open a fresh conn if none idle ───────────────
        //   cppcache: createPoolConnectionToAEndPoint(...) → fallback to
        //   currentEndpoint->createNewConnection (temporary, putConnInPool=false)
        //   if pool-cap reached. Phase 1.1 collapses both branches into one
        //   pool-tracked conn (no maxConn limiter yet).
        var putConnInPool = true;
        conn ??= await CreatePoolConnectionToAEndPointAsync(endpoint, ct).ConfigureAwait(false);

        if (conn is null)
        {
            // cppcache: setConnectionStatus(false) + LOGFINE("3Failed to connect").
            endpoint.SetConnected(false);
            throw new GeodeException(
                $"ThinClientPoolDM: could not obtain a connection to {endpoint.Name}.");
        }

        try
        {
            // ─── Step 3: actual chunked wire I/O ──────────────────
            // cppcache: currentEndpoint->sendRequestConnWithRetry(request, reply, conn, true).
            // chunked overload feeds each arriving chunk to chunkedResult;
            // returns a synthetic TcrMessage carrying just the reply
            // header (MessageType / TransactionId).
            var reply = await conn.SendRequestAsync(request, chunkedResult, ct).ConfigureAwait(false);

            // ─── Step 4: happy path — return conn to endpoint queue ──
            //   cppcache: putConnInPool ? put(conn, false) : close+delete(conn).
            if (putConnInPool)
            {
                await PutInQueueAsync(conn, ct).ConfigureAwait(false);
            }
            else
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }

            return reply;
        }
        catch
        {
            // cppcache: setConnectionStatus(false) + removeEPConnections(1)
            // + removeEPFromMetadataIfError. Phase 1.5 will classify the
            // GfErrType and decide whether to truly mark the endpoint
            // down vs. retry on another conn; Phase 1.1 is conservative
            // — any failure on a conn drops it and marks endpoint down.
            endpoint.SetConnected(false);
            if (putConnInPool)
            {
                Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
            }
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ── ThinClientBaseDM pure abstract ──────────────────────────

    /// <summary>
    /// DM-level send: pick an endpoint and route the request through
    /// it. Mirrors cppcache
    /// <c>ThinClientPoolDM::sendSyncRequest(request, reply, ...)</c>
    /// (<c>ThinClientPoolDM.cpp:1380-1500</c>) — the path every region
    /// op (Put / Get / ContainsKey / Destroy) takes when the caller
    /// does not pin a specific endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 1.2 slice — single endpoint, no failover, no retry.
    /// cppcache wraps <see cref="SendRequestToEndpointAsync"/> in a
    /// do-while loop driven by <c>isFatalError</c> classification +
    /// <c>selectEndpoint(excludeServers)</c>; the retry logic lands in
    /// Phase 1.5 once <c>GfErrType</c> taxonomy + <c>excludeServers</c>
    /// thread through.
    /// </para>
    /// <para>
    /// <paramref name="attemptFailover"/> and
    /// <paramref name="isBackgroundThread"/> are accepted for cppcache
    /// signature parity but currently ignored — failover is Phase 1.5,
    /// background-thread stats hooks are Phase 1.5 stats work.
    /// </para>
    /// </remarks>
    public override async Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);

        _ = attemptFailover;        // Phase 1.5: failover loop.
        _ = isBackgroundThread;     // Phase 1.5: stats hook.

        logger.LogDebug(
            "ThinClientPoolDM::sendSyncRequest type={MessageType} txId={TxId}",
            request.MessageType, request.TransactionId);

        // Step 1 — pick an endpoint. cppcache's selectEndpoint takes
        //   excludeServers + currentServer; MVP needs neither (single
        //   endpoint, no retry).
        // Op-layer caller: no retry context, pass empty excludeServers.
        // The op's own outer retry (Phase 1.5 sendSyncRequest wrap) will
        // own the set when wired.
        var location = await SelectEndpointAsync([], ct).ConfigureAwait(false);

        // Step 2 — get-or-create the pool's TcrEndpoint reference.
        //   cppcache does this implicitly inside selectEndpoint; we
        //   keep the addEP step explicit.
        var endpoint = await AddEPAsync(location, ct).ConfigureAwait(false);

        // Step 3 — delegate to the endpoint-pinned send path. That
        //   helper handles conn borrow / fallback-create / send /
        //   put-back / disconnect-on-error already; nothing more for
        //   this layer to do in MVP.
        return await SendRequestToEndpointAsync(request, endpoint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Chunked-reply overload. Phase 1.3.b skeleton &#x2014; throws
    /// <see cref="NotImplementedException"/> until the
    /// <see cref="TcrConnection"/> reader-loop refactor lands so
    /// <c>_pendingReplies</c> can route arriving chunks to
    /// <paramref name="chunkedResult"/>.
    /// </summary>
    public override async Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default)
    {
        // ─── Step 1: guards ──────────────────────────────────
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(chunkedResult);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);

        _ = attemptFailover;        // Phase 1.5: failover loop.
        _ = isBackgroundThread;     // Phase 1.5: stats hook.

        logger.LogDebug(
            "ThinClientPoolDM::sendSyncRequest (chunked) type={MessageType} txId={TxId}",
            request.MessageType, request.TransactionId);

        // ─── Step 2: SelectEndpoint ──────────────────────────
        // cppcache's selectEndpoint takes excludeServers + currentServer
        // for failover; MVP needs neither (single endpoint, no retry).
        // Op-layer caller: no retry context, pass empty excludeServers.
        // The op's own outer retry (Phase 1.5 sendSyncRequest wrap) will
        // own the set when wired.
        var location = await SelectEndpointAsync(new HashSet<DnsEndPoint>(), ct).ConfigureAwait(false);

        // ─── Step 3: AddEP (get-or-create TcrEndpoint) ───────
        // cppcache does this implicitly inside selectEndpoint; we
        // keep the addEP step explicit.
        var endpoint = await AddEPAsync(location, ct).ConfigureAwait(false);

        // ─── Step 4: forward to endpoint-pinned chunked send ─
        // The overload still NIE inside (borrow conn → chunked wire I/O
        // → put-back); next todo fills it in.
        return await SendRequestToEndpointAsync(request, chunkedResult, endpoint, ct).ConfigureAwait(false);
    }

    public bool IsDestroyed => Volatile.Read(ref _isDestroyed) != 0;

    public string Name => xmlPool.Name;
    public IQueryService QueryService =>
        LazyInitializer.EnsureInitialized(
            ref _queryService,
            () => ActivatorUtilities.CreateInstance<RemoteQueryService>(serviceProvider, this));

#pragma warning disable CS0169, CS0414, CS0649, CS9113 // placeholder fields mirroring ThinClientPoolDM; wired up phase by phase
    // ── Single-hop metadata (Phase 4) ──
    private object? _clientMetadataService;        // m_clientMetadataService

    // ── HA subscription (Phase 2+) — inherited from base TCCM via composition ──
    private object? _redundancyManager;            // m_redundancyManager

    // ── Sticky transactions (Phase 6) ──
    private object? _stickyManager;                // ThinClientStickyManager
    private bool _isSticky;                        // m_sticky flag

    // ── State flags (ThinClientPoolDM.hpp:203-204) ──

    private int _destroyPending;                   // m_destroyPending (Interlocked 0/1)
#pragma warning restore CS0169, CS0414, CS0649


    #region Ping

    private Task? _pingLoop;
    private int _pingTickCount;
    private int _pingSuccessCount;
    private PeriodicTimer? _pingTimer;
    private readonly SemaphoreSlim _pingSignal = new(0, int.MaxValue);

    /// <summary>
    /// Schedule the periodic ping loop, mirroring cppcache
    /// <c>ThinClientPoolDM.cpp:269-290</c>. cppcache splits this in two:
    /// a long-running <c>pingServer</c> Task that blocks on
    /// <c>ping_semaphore_.acquire()</c>, plus a <c>FunctionExpiryTask</c>
    /// scheduled by <c>ExpiryTaskManager</c> that releases the semaphore
    /// every <c>PingInterval</c>. We collapse to one loop driven by
    /// <see cref="PeriodicTimer"/>; <see cref="_pingSignal"/> stays
    /// declared so Phase 1.5's failover path can release it for an
    /// immediate probe (then this loop becomes <c>WaitAny(timer, signal)</c>).
    /// </summary>
    /// <remarks>
    /// Interval resolution mirrors cppcache <c>getPingInterval()</c>:
    /// per-pool override (<see cref="CachePoolOptions.PingInterval"/>)
    /// wins, otherwise fall back to the system default
    /// (<see cref="PoolOptions.PingInterval"/>, 10 s).
    /// Interval <c>&lt;= 0</c> disables ping entirely (cppcache L286-289).
    /// </remarks>
    private void SchedulePingLoop()
    {
        var pingInterval = xmlPool.PingInterval ?? options.Pool.PingInterval;
        if (pingInterval > TimeSpan.Zero)
        {
            logger.LogDebug(
                "ThinClientPoolDM::startBackgroundThreads: Scheduling ping task at {Interval}",
                pingInterval);
            _pingTimer = new PeriodicTimer(pingInterval);
            _pingLoop = PingLoopAsync(_backgroundCts.Token);
        }
        else
        {
            logger.LogDebug(
                "ThinClientPoolDM::startBackgroundThreads: Not scheduling ping task as ping interval {Interval}",
                pingInterval);
        }
    }

    /// <summary>
    /// Test-only: number of ping-loop ticks that have entered
    /// <see cref="PingServerLocalAsync"/>. Lets integration tests assert
    /// the loop is alive without scraping logs. Phase 1.5 stats wrapper
    /// (cppcache <c>PoolStats</c>) will subsume this.
    /// </summary>
    internal int PingTickCount => Volatile.Read(ref _pingTickCount);

    /// <summary>
    /// Test-only: number of <see cref="TcrEndpoint.PingAsync"/> calls that
    /// returned without throwing AND left the endpoint still
    /// <see cref="TcrEndpoint.IsConnected"/> true. Subsumed by Phase 1.5
    /// stats once <c>PoolStats</c> lands.
    /// </summary>
    internal int PingSuccessCount => Volatile.Read(ref _pingSuccessCount);

    /// <summary>
    /// Periodic ping loop. Mirrors cppcache
    /// <c>ThinClientPoolDM::pingServer</c>
    /// (<c>ThinClientPoolDM.cpp:2070-2083</c>): each tick walks every
    /// connected endpoint and probes it with <c>MessageType.Ping</c>.
    /// </summary>
    private async Task PingLoopAsync(CancellationToken ct)
    {
        var initialDelay = TimeSpan.FromSeconds(1);
        logger.LogDebug("Starting ping loop for pool {Pool}", Name);
        try
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            do
            {
                try
                {
                    await PingServerLocalAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // One bad tick must not kill the loop — next tick retries.
                    logger.LogWarning(ex, "Ping tick failed for pool {Pool}", Name);
                }
            }
            while (await _pingTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // graceful shutdown via _backgroundCts.Cancel().
        }
        logger.LogDebug("Ending ping loop for pool {Pool}", Name);
    }
    /// <summary>
    /// One ping sweep: probe every connected endpoint and prune the
    /// pool's references to any that fall offline. Mirrors cppcache
    /// <c>ThinClientPoolDM::pingServerLocal</c>
    /// (<c>ThinClientPoolDM.cpp:2028-2040</c>).
    /// </summary>
    /// <remarks>
    /// cppcache holds <c>m_endpointsLock</c> for the whole sweep because
    /// <c>std::map</c> isn't safe for concurrent iteration; our
    /// <see cref="_endpoints"/> is a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    /// so a snapshot enumeration is safe and the sweep won't block
    /// <see cref="AddEPAsync"/>.
    /// </remarks>
    private async Task PingServerLocalAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _pingTickCount);
        logger.LogTrace("Ping sweep for pool {Pool}: {Count} endpoint(s)", Name, _endpoints.Count);

        foreach (var (_, endpoint) in _endpoints)
        {
            ct.ThrowIfCancellationRequested();

            if (!endpoint.IsConnected)
            {
                // cppcache: pingServerLocal skips disconnected endpoints
                // (the test is inside the loop body at L2032).
                continue;
            }

            await endpoint.PingAsync(this, ct).ConfigureAwait(false);

            if (endpoint.IsConnected)
            {
                Interlocked.Increment(ref _pingSuccessCount);
            }

            if (!endpoint.IsConnected)
            {
                // cppcache (ThinClientPoolDM.cpp:2034-2037): the ping just
                // flipped the endpoint's connected_ bit to false → drop the
                // pool's references on its conns + subscription.
                // TODO Phase 1.5: RemoveEPConnections(endpoint);
                //                 RemoveCallbackConnection(endpoint);
                logger.LogDebug("Ping flipped endpoint {Endpoint} to disconnected; cleanup deferred to Phase 1.5", endpoint.Name);
            }
        }
    }

    #endregion

    #region Connection Manager

    private Task? _connManageLoop;
    private readonly SemaphoreSlim _connManageSignal = new(0, int.MaxValue);

    /// <summary>
    /// Periodic conn-management loop. Mirrors cppcache
    /// <c>ThinClientPoolDM::manageConnectionsInternal()</c>
    /// (<c>ThinClientPoolDM.cpp:554-575</c>): on each tick run
    /// cleanStaleConnections + RestoreMinConnectionsAsync +
    /// cleanStickyConnections. cppcache schedules it with a 10 s
    /// initial delay; we mirror that by awaiting the interval
    /// before the first iteration.
    /// </summary>
    private async Task ConnManageLoopAsync(CancellationToken ct)
    {
        // cppcache schedules the conn-management task with a fixed 1 s
        // initial delay and then repeats every IdleTimeout
        // (ThinClientPoolDM.cpp:343-344, `schedule(task, seconds(1),
        // idle)`). Pre-opens MinConnections within ~1 s of init so the
        // first user op finds an aged connection in the queue instead
        // of having to lazy-open a fresh one (which the server hasn't
        // finished registering, → RegionDestroyedException on the very
        // first request).
        var initialDelay = TimeSpan.FromSeconds(1);
        var interval = xmlPool.IdleTimeout;
        try
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await CleanStaleConnectionsAsync(ct).ConfigureAwait(false);
                    await RestoreMinConnectionsAsync(ct).ConfigureAwait(false);
                    // TODO Phase 6:    await CleanStickyConnectionsAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Survive transient errors so a single bad tick
                    // doesn't kill the loop. Phase 1.5: log via
                    // ILogger.
                }

                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // graceful shutdown via _backgroundCts.Cancel().
        }
    }

    /// <summary>
    /// One sweep of the idle queue: drop / replace stale connections.
    /// Mirrors cppcache <c>ThinClientPoolDM::cleanStaleConnections</c>
    /// (<c>ThinClientPoolDM.cpp:402-~500</c>). Called once per
    /// <see cref="ConnManageLoopAsync"/> tick before
    /// <see cref="RestoreMinConnectionsAsync"/>.
    /// </summary>
    private enum RemovalReason { LoadConditioning, Idle }

    private async Task CleanStaleConnectionsAsync(CancellationToken ct)
    {
        // Two staleness reasons:
        //   load conditioning — age > LoadConditioningInterval (forced rotation).
        //   idle              — unused > IdleTimeout AND _poolSize > Min (shrink).

        // ── Step B — Classify (cppcache L412-436) ────────────────────
        var idle = xmlPool.IdleTimeout;
        var loadCond = xmlPool.LoadConditioningInterval;
        var min = xmlPool.MinConnections;

        // Bound the sweep by initial queue depth (cppcache `availableConns = size()`):
        // own re-pushes don't re-inspect; other-thread returns wait for next tick.
        var snapshot = _opConnections.Reader.Count;
        var removelist = new List<(TcrConnection Conn, RemovalReason Reason)>();
        var savedConns = 0;

        for (var i = 0; i < snapshot; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!_opConnections.Reader.TryRead(out var conn))
            {
                // Drained early (cppcache `getNoWait → nullptr`).
                break;
            }

            // cppcache canItBeDeleted (L2107-2121): idle threshold falls back
            // to loadCond when shorter / disabled. Subscription-queue guard
            // (L2124-2140) is Phase 2+ HA. Split per reason so Step C can
            // pick the right counter (cppcache lumps both into incLoadCondDisconnects).
            var effectiveIdle = (loadCond > TimeSpan.Zero && (loadCond < idle || idle <= TimeSpan.Zero))
                ? loadCond
                : idle;

            if (conn.HasExpired(loadCond))
            {
                removelist.Add((conn, RemovalReason.LoadConditioning));
            }
            else if (conn.IsIdle(effectiveIdle) && Volatile.Read(ref _poolSize) > min)
            {
                removelist.Add((conn, RemovalReason.Idle));
            }
            else
            {
                await _opConnections.Writer.WriteAsync(conn, ct).ConfigureAwait(false);
                savedConns++;
            }
        }

        // ── Step C — Replace vs delete (cppcache L444-499) ───────────
        var replaceCount = min - savedConns;
        foreach (var (conn, reason) in removelist)
        {
            ct.ThrowIfCancellationRequested();

            if (replaceCount <= 0)
            {
                // Pure shrink — savedConns covers Min, close without replacement.
                await conn.CloseAsync(_keepAlive, ct).ConfigureAwait(false);
                Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                switch (reason)
                {
                    case RemovalReason.LoadConditioning: _stats.LoadConditioningDisconnect(); break;
                    case RemovalReason.Idle: _stats.IdleDisconnect(); break;
                }
            }
            else
            {
                // cppcache parity: pass empty excludeServers + conn as
                // currentServer hint. When SelectEndpoint picks the same
                // endpoint, the recycle path inside CreatePoolConnectionAsync
                // returns the same conn (no handshake waste); when it picks
                // a different one, we get a real rotation.
                var newConn = await CreatePoolConnectionAsync(
                    [], currentServer: conn, ct).ConfigureAwait(false);
                if (newConn is not null)
                {
                    await _opConnections.Writer.WriteAsync(newConn, ct).ConfigureAwait(false);
                    // newConn == conn means cppcache recycle; only close on real swap.
                    if (!ReferenceEquals(newConn, conn))
                    {
                        await conn.CloseAsync(_keepAlive, ct).ConfigureAwait(false);
                        Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                        _stats.LoadConditioningDisconnect();
                        _stats.LoadConditioningConnect();
                    }
                }
                else if (conn.HasExpired(loadCond))
                {
                    // Replacement failed AND past loadCond → close anyway (doomed).
                    await conn.CloseAsync(_keepAlive, ct).ConfigureAwait(false);
                    Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                    _stats.LoadConditioningDisconnect();
                }
                else
                {
                    // Replacement failed, not expired → reset age + push back
                    // (cppcache :488); else re-elected every sweep.
                    conn.UpdateCreationTime();
                    await _opConnections.Writer.WriteAsync(conn, ct).ConfigureAwait(false);
                }
                replaceCount--;
            }
        }

    }

    #endregion

    #region Locator

    private readonly SemaphoreSlim _updateLocatorSignal = new(0, int.MaxValue);
    private Task? _updateLocatorLoop;
    private PeriodicTimer? _updateLocatorTimer;
    private ThinClientLocatorHelper? _locatorHelper;

    /// <summary>
    /// Maybe launch <see cref="UpdateLocatorLoopAsync"/>. Mirrors
    /// cppcache <c>ThinClientPoolDM.cpp:292-302</c>: only fires when
    /// the pool actually has locators configured (no locators ⇒
    /// nothing to refresh). Default 5s baked into
    /// <see cref="CachePoolOptions.UpdateLocatorListInterval"/>
    /// (cppcache <c>PoolFactory.cpp:51</c>); validator rejects
    /// negatives. Interval <c>== 0</c> disables (matches cppcache
    /// <c>L286-289</c>).
    /// </summary>
    private void ScheduleUpdateLocatorLoop()
    {
        if (xmlPool.Locators.Count == 0) return;

        // Build the helper once we know locators are configured. cppcache
        // ThinClientPoolDM ctor builds m_locHelper unconditionally; we
        // gate on Locators so the field stays null when the pool runs
        // static-server mode (Step E's SelectEndpointAsync locator branch
        // never fires either, so no consumer of _locatorHelper exists).
        // CacheHostPortOptions → ServerLocation conversion is the
        // options-layer ↔ wire-layer boundary.
        var initialLocators = xmlPool.Locators
            .Select(l => new ServerLocation(l.Host, l.Port))
            .ToList();
        // cppcache: getConnRetries() reads m_poolDM->getRetryAttempts(),
        // falling back to 3 when ≤0 (ThinClientLocatorHelper.cpp:66-68).
        // We pass it once at construction — Phase 1.5 MVP doesn't reload.
        var connectionRetries = xmlPool.RetryAttempts ?? 0;
        _locatorHelper = ActivatorUtilities.CreateInstance<ThinClientLocatorHelper>(
            serviceProvider, initialLocators, connectionRetries);

        var updateInterval = xmlPool.UpdateLocatorListInterval;
        if (updateInterval <= TimeSpan.Zero)
        {
            logger.LogDebug(
                "ThinClientPoolDM::startBackgroundThreads: Not scheduling updateLocatorList as interval {Interval}",
                updateInterval);
            return;
        }

        logger.LogDebug(
            "ThinClientPoolDM::startBackgroundThreads: Scheduling updateLocatorList task at {Interval}",
            updateInterval);
        _updateLocatorTimer = new PeriodicTimer(updateInterval);
        _updateLocatorLoop = UpdateLocatorLoopAsync(_backgroundCts.Token);
    }

    /// <summary>
    /// Periodic locator-list refresh loop. Mirrors cppcache
    /// <c>ThinClientPoolDM::updateLocatorList</c>
    /// (<c>ThinClientPoolDM.cpp:2042-2053</c>): each tick asks every
    /// configured locator who's alive and updates the pool's known
    /// locator list accordingly. cppcache's body is a
    /// <c>semaphore.acquire()</c>-blocked task whose semaphore is
    /// released by a separate <c>FunctionExpiryTask</c>; we collapse
    /// that two-piece pattern into a single <see cref="PeriodicTimer"/>
    /// loop (same shape as <see cref="PingLoopAsync"/>).
    /// </summary>
    private async Task UpdateLocatorLoopAsync(CancellationToken ct)
    {
        // cppcache schedules with the same fixed 1 s initial delay as
        // ping; first refresh fires ~1 s after pool init rather than
        // after a full interval.
        var initialDelay = TimeSpan.FromSeconds(1);

        // cppcache LOGFINE("Starting updateLocatorList thread for pool %s", ...)
        logger.LogDebug("Starting updateLocatorList loop for pool {Pool}", Name);
        try
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            do
            {
                try
                {
                    await UpdateLocatorsLocalAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // One bad refresh must not kill the loop — next tick retries.
                    logger.LogWarning(ex, "updateLocatorList tick failed for pool {Pool}", Name);
                }
            }
            while (await _updateLocatorTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // graceful shutdown via _backgroundCts.Cancel().
        }
        logger.LogDebug("Ending updateLocatorList loop for pool {Pool}", Name);
    }

    /// <summary>
    /// One locator-list refresh. Mirrors cppcache
    /// <c>(m_locHelper)-&gt;updateLocators(getServerGroup())</c>
    /// (<c>ThinClientPoolDM.cpp:2048</c>): asks the configured
    /// locators for the current authoritative locator set so the pool
    /// can drop dead locators and pick up newly-added ones.
    /// </summary>
    /// <remarks>
    /// Stub until <c>ThinClientLocatorHelper</c> lands; the loop's
    /// scaffolding (timer, cancellation, error survival) is verified
    /// first so its replacement only has to fill in the wire I/O.
    /// </remarks>
    private async Task UpdateLocatorsLocalAsync(CancellationToken ct)
    {
        // _locatorHelper is non-null here: ScheduleUpdateLocatorLoop
        // both builds it and launches this loop only when locators
        // are configured (same gate, same call site).
        using var activity = _stats.StartLocatorListRequest();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _locatorHelper!.UpdateLocatorsAsync(xmlPool.ServerGroup, ct).ConfigureAwait(false);
        }
        finally
        {
            _stats.LocatorListRequest(stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Query the pool's <see cref="ThinClientLocatorHelper"/> for one
    /// server. Mirrors cppcache <c>ThinClientPoolDM::selectEndpoint</c>
    /// locator branch (<c>ThinClientPoolDM.cpp:580-604</c>).
    /// </summary>
    /// <remarks>
    /// Phase 1.5 MVP: empty <c>excludeServers</c> and no
    /// <c>currentServer</c> — neither failover-driven retry exclusion
    /// nor server replacement is wired in yet.
    /// </remarks>
    private async Task<DnsEndPoint> SelectEndpointFromLocatorAsync(
        HashSet<DnsEndPoint> excludeServers,
        CancellationToken ct)
    {
        logger.LogDebug("ThinClientPoolDM: Asking locator for server from group [{Group}]", xmlPool.ServerGroup);

        // Convert pool-layer DnsEndPoint set → wire-layer ServerLocation list
        // at the helper boundary. ServerLocation is a value record so the
        // List is cheap to materialise; ToList avoids exposing IEnumerable
        // ordering quirks across the helper call.
        var excludeWire = excludeServers.Select(e => new ServerLocation(e.Host, e.Port)).ToList();

        // cppcache ThinClientPoolDM.cpp:587 — incLoctorRequests() before
        // helper call. Maps to ClientConnectionRequest wire RPC.
        using var activity = _stats.StartClientConnectionRequest();
        var stopwatch = Stopwatch.StartNew();
        ServerLocation server;
        try
        {
            server = await _locatorHelper!.GetEndpointForNewFwdConnAsync(xmlPool.ServerGroup, excludeWire, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _stats.ClientConnectionRequest(stopwatch.Elapsed);
        }

        var endpoint = new DnsEndPoint(server.Host, server.Port);

        logger.LogDebug("ThinClientPoolDM: Locator returned endpoint [{Host}:{Port}]", endpoint.Host, endpoint.Port);
        return endpoint;
    }

    #endregion
}
