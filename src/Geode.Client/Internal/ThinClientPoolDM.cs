using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Services;
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

internal class ThinClientPoolDM(
    IServiceProvider serviceProvider,
    ILogger<ThinClientPoolDM> logger,
    CachePoolOptions xmlPool,
    GeodeClientOptions options,
    TcrConnectionManager connManager)
    : ThinClientBaseDM(connManager, region: null), IPool
{

    /// <summary>
    /// Cancellation source for every background loop the pool spawns;
    /// <see cref="DestroyAsync"/> cancels it to signal graceful shutdown.
    /// </summary>
    private readonly CancellationTokenSource _backgroundCts = new();

    /// <summary>
    /// <see cref="CachePoolOptions.MaxConnections"/> slot reservation:
    /// <c>WaitAsync(<see cref="CachePoolOptions.FreeConnectionTimeout"/>)</c>
    /// on open, <c>Release</c> on close. <c>null</c> when unbounded.
    /// </summary>
    private readonly SemaphoreSlim? _capSlots = xmlPool.MaxConnections is int cap
        ? new SemaphoreSlim(cap, cap)
        : null;

    /// <summary>
    /// Reserve one <see cref="_capSlots"/> slot, waiting up to
    /// <see cref="CachePoolOptions.FreeConnectionTimeout"/>. No-op when
    /// <see cref="_capSlots"/> is <c>null</c> (unbounded pool). Throws
    /// <see cref="AllConnectionsInUseException"/> on timeout. The wait
    /// is bracketed by <see cref="_connectionWaitsInProgress"/>
    /// inc/dec — mirrors cppcache <c>getConnectionFromQueue</c>
    /// (<c>ThinClientPoolDM.cpp:1819, 1835</c>) so the
    /// <c>ConnectionWaitsInProgress</c> gauge reflects threads queued
    /// on the pool-wide cap (inc/dec safe across cancellation and
    /// throw via <c>try/finally</c>).
    /// </summary>
    private async Task AcquirePoolCapSlotAsync(CancellationToken ct)
    {
        if (_capSlots is null) return;

        // cppcache (:1819-1820) bumps the gauge (#12) AND the cumulative
        // counter (#13) at the entry point, then records elapsed time
        // (#14) after `getUntil` (:1822-1833). We collapse #13 + #14 into
        // a single Histogram: .Count subsumes the wait-attempt counter,
        // sum subsumes the cumulative time. Recording sits in `finally`
        // so timeouts / cancellations still tick (matches the
        // LocatorListRequestTime pattern).
        Interlocked.Increment(ref _connectionWaitsInProgress);
        var stopwatch = Stopwatch.StartNew();
        bool acquired;
        try
        {
            acquired = await _capSlots.WaitAsync(xmlPool.FreeConnectionTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _connectionWaitsInProgress);
            _stats.ConnectionWait(stopwatch.Elapsed);
        }
        if (!acquired)
        {
            throw new AllConnectionsInUseException(
                $"Pool '{xmlPool.Name}': MaxConnections={xmlPool.MaxConnections} reached.");
        }
    }

    /// <summary>
    /// Whether to clear cached PDX type IDs when the pool fully disconnects.
    /// Mirrors cppcache <c>clear_pdx_registry_</c>; sourced from
    /// <see cref="PdxOptions.ClearTypeIdsOnDisconnect"/>. Reader
    /// (<c>decConnectedEndpoints</c> → <c>clearPdxTypeRegistry</c>) is Phase 2+.
    /// </summary>
    private bool _clearPdxRegistry;

    /// <summary>
    /// PR single-hop metadata service. Built when
    /// <see cref="CachePoolOptions.PrSingleHopEnabled"/>; lifecycle paired
    /// with <see cref="StartBackgroundThreads"/> / <see cref="DestroyAsync"/>.
    /// </summary>
    private ClientMetadataService? _clientMetadataService;

    /// <summary>
    /// Pool's view onto TCCM-owned <see cref="TcrEndpoint"/> instances —
    /// tracks which endpoints this pool holds a ref on so destroy knows
    /// what to release.
    /// </summary>
    private readonly ConcurrentDictionary<DnsEndPoint, TcrEndpoint> _endpoints = new();

    /// <summary>
    /// Serialises endpoint selection in <see cref="SelectEndpointAsync"/>
    /// (round-robin cursor + locator pick). Mirrors cppcache
    /// <c>m_endpointSelectionLock</c>.
    /// </summary>
    private readonly Lock _endpointSelectionLock = new();

    /// <summary>
    /// 0 = <see cref="InitAsync"/> not run, 1 = ran. Mirrors cppcache
    /// pool DM's one-shot init guard; gated by
    /// <see cref="Interlocked.Exchange(ref int, int)"/>.
    /// </summary>
    private int _initGuard;

    /// <summary>
    /// 0 / 1 destroy guard, gated by <see cref="Interlocked.Exchange(ref int, int)"/>.
    /// </summary>
    private int _isDestroyed;

    /// <summary>
    /// cppcache <c>m_isMultiUserMode</c> — from <see cref="CachePoolOptions.MultiuserAuthentication"/>.
    /// </summary>
    private bool _isMultiUserMode;

    /// <summary>
    /// cppcache <c>m_isSecurityOn</c> — true when the cache has any security-* property set (proxy until Phase 3 auth callback lands).
    /// </summary>
    private bool _isSecurityOn;

    /// <inheritdoc/>
    public override bool IsMultiUserMode => _isMultiUserMode;

    /// <inheritdoc/>
    public override bool IsSecurityOn => _isSecurityOn;

    /// <summary>
    /// Bump <see cref="_connectedEndpoints"/>. Mirrors cppcache
    /// <c>ThinClientPoolDM::incConnectedEndpoints</c>
    /// (<c>ThinClientPoolDM.cpp:2055-2059</c>). Fires from
    /// <see cref="TcrEndpoint.SetConnected"/>'s broadcast on a real
    /// disconnected&#x2192;connected transition.
    /// </summary>
    public override void IncConnectedEndpoints()
    {
        var val = Interlocked.Increment(ref _connectedEndpoints);
        logger.LogDebug(
            "Pool {Pool} has incremented to {Count} the number of connected endpoints",
            Name, val);
    }

    /// <summary>
    /// Decrement <see cref="_connectedEndpoints"/>. Mirrors cppcache
    /// <c>ThinClientPoolDM::decConnectedEndpoints</c>
    /// (<c>ThinClientPoolDM.cpp:2061-2068</c>). Fires from
    /// <see cref="TcrEndpoint.SetConnected"/>'s broadcast on a real
    /// connected&#x2192;disconnected transition.
    /// </summary>
    /// <remarks>
    /// cppcache also clears the PDX type registry when the count hits
    /// zero AND <c>clear_pdx_registry_</c> is true — that flag tracks
    /// whether any PDX types were registered against now-dead servers.
    /// Phase 2+ (PDX serialisation) work; TODO inline.
    /// </remarks>
    public override void DecConnectedEndpoints()
    {
        var val = Interlocked.Decrement(ref _connectedEndpoints);
        logger.LogDebug(
            "Pool {Pool} has decremented to {Count} the number of connected endpoints",
            Name, val);
        // TODO Phase 2+: if (val <= 0 && _clearPdxRegistry) ClearPdxTypeRegistry();
        //   cppcache ThinClientPoolDM.cpp:2065-2067.
    }

    /// <summary>
    /// <c>keepAlive</c> intent stashed in <see cref="DestroyAsync"/> for
    /// each conn's <see cref="TcrConnection.CloseAsync"/>.
    /// </summary>
    private bool _keepAlive;

    /// <summary>
    /// Idle conn queue. Inlined mirror of cppcache
    /// <c>ConnectionQueue&lt;TcrConnection&gt;::queue_</c>
    /// (<c>ThinClientPoolDM.cpp:2156</c>); chosen over
    /// <see cref="System.Threading.Channels.Channel{T}"/> so per-endpoint
    /// ops (<see cref="GetFromEPAsync"/>, future
    /// <c>removeEPConnections</c>) can iterate-and-erase by predicate.
    /// Guarded by <see cref="_opConnLock"/>.
    /// </summary>
    private readonly LinkedList<TcrConnection> _opConnections = new();

    /// <summary>
    /// Mutex for <see cref="_opConnections"/>; mirror of cppcache <c>mutex_</c>.
    /// </summary>
    private readonly Lock _opConnLock = new();

    /// <summary>
    /// Current pool conn count (cppcache <c>m_poolSize</c>).
    /// Bumped in <see cref="CreatePoolConnectionAsync"/> after handshake,
    /// decremented on every close site. Surfaced via <see cref="PoolSize"/>.
    /// </summary>
    private int _poolSize = 0;

    /// <summary>
    /// Current count of endpoints whose <see cref="TcrEndpoint.IsConnected"/>
    /// is true. Mirrors cppcache <c>connected_endpoints_</c>
    /// (<c>ThinClientPoolDM.cpp:2055-2068</c>). Bumped / decremented by
    /// <see cref="IncConnectedEndpoints"/> / <see cref="DecConnectedEndpoints"/>,
    /// which fire from <see cref="TcrEndpoint.SetConnected"/>'s broadcast
    /// on real 0&#x2194;1 transitions. Surfaced via the
    /// <c>ConnectedServers</c> ObservableGauge.
    /// </summary>
    private int _connectedEndpoints = 0;

    /// <summary>
    /// Threads currently inside the pool-wide <see cref="_capSlots"/> wait
    /// in <see cref="CreatePoolConnectionAsync"/> /
    /// <see cref="CreatePoolConnectionToAEndPointAsync"/>. Mirrors cppcache
    /// <c>connectionWaitsInProgress</c> (<c>PoolStatistics.cpp:74-76</c>,
    /// bumped in <c>getConnectionFromQueue</c> at <c>:1819</c>). Surfaced via
    /// the <c>ConnectionWaitsInProgress</c> ObservableGauge. cppcache
    /// instruments only the pool-wide cap; per-EP cap waits
    /// (<see cref="TcrEndpoint.AcquireSlotAsync"/>) are our addition and
    /// not counted here.
    /// </summary>
    private int _connectionWaitsInProgress = 0;

    /// <summary>
    /// In-flight pool ops — incremented at <see cref="SendSyncRequestCoreAsync"/>
    /// entry, decremented in <c>finally</c>. Mirrors cppcache
    /// <c>m_clientOps</c> tracked via <c>setCurClientOps(++m_clientOps)</c> /
    /// <c>setCurClientOps(--m_clientOps)</c> around <c>sendSyncRequest</c>
    /// (<c>ThinClientPoolDM.cpp:1272, 1519, 1538</c>). Surfaced via the
    /// <c>ClientOpsInProgress</c> ObservableGauge.
    /// </summary>
    private int _clientOpsInProgress = 0;

    /// <summary>
    /// Pool-scoped query service. Lazy-built on first
    /// <see cref="QueryService"/> access (primary-ctor field-init can't
    /// reference <c>this</c>). Mirrors cppcache
    /// <c>m_remoteQueryServicePtr</c>.
    /// </summary>
    private RemoteQueryService? _queryService;

    /// <summary>
    /// Static-server round-robin cursor for
    /// <see cref="SelectEndpointAsync"/>; read + post-increment-with-wrap
    /// under <see cref="_endpointSelectionLock"/>. Mirrors cppcache
    /// <c>m_server</c>. Init-time random start so concurrent client
    /// startups don't all hit <c>Servers[0]</c> first.
    /// </summary>
    private int _server = xmlPool.Servers.Count > 0
        ? Random.Shared.Next(xmlPool.Servers.Count)
        : 0;

    /// <summary>
    /// Pool stats sink (Meter + ActivitySource). Mirrors cppcache
    /// <c>m_stats</c> / <c>PoolStats</c>.
    /// </summary>
    private readonly PoolStatistics _stats = ActivatorUtilities.CreateInstance<PoolStatistics>(serviceProvider, xmlPool.Name);

    /// <summary>
    /// Sticky-transaction connection manager. Built unconditionally in
    /// <see cref="InitAsync"/> (cppcache ctor L209); cleanup paired with
    /// <see cref="DestroyAsync"/> step 6b. <c>protected</c> so the
    /// sticky-pool subclass can dispatch sticky-conn ops to it.
    /// </summary>
    protected ThinClientStickyManager? _stickyManager;

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
        // via _capSlots: AcquirePoolCapSlotAsync reserves a slot atomically,
        // waiting up to FreeConnectionTimeout for a returning conn before
        // throwing. finally releases unless ownership transferred to a
        // freshly-opened conn (success path clears releaseSlot).
        await AcquirePoolCapSlotAsync(ct).ConfigureAwait(false);

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

                // Per-endpoint cap (cppcache TcrEndpoint::m_maxConnections).
                // Acquired AFTER the recycle check (recycle reuses an existing
                // conn → its slot stays). On endpoint-capped, blacklist & loop
                // to the next server rather than throwing — pool-wide may
                // still have headroom elsewhere.
                if (!await endpoint.AcquireSlotAsync(xmlPool.FreeConnectionTimeout, ct).ConfigureAwait(false))
                {
                    logger.LogDebug("Endpoint {Endpoint} ConnectionPoolSize cap reached, trying next", endpoint.Name);
                    excludeServers.Add(location);
                    continue;
                }
                var releaseEndpointSlot = true;
                try
                {
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
                        // locator dead). Propagate; slots released by both finallys.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // cppcache isFatalError ∪ transient (L1772-1786): blacklist
                        // this server and try the next. Covers SocketException,
                        // IOException, TimeoutException, NotConnectedException, plus
                        // CacheServerException ("fatal-but-keep-trying-next" — the
                        // next server might be healthy). Pool-wide slot stays
                        // reserved (carries to next iteration); per-EP slot is
                        // released via the surrounding finally before `continue`.
                        logger.LogDebug(ex, "Failed to open conn to {Endpoint}, retrying with next", endpoint.Name);
                        excludeServers.Add(location);
                        continue;
                    }

                    endpoint.SetConnected(true);
                    // Wire poolDM back-ref so TcrConnection.ReceiveAsync can route
                    // #20 ReceivedBytes back to this pool's stats. cppcache sets
                    // poolDM_ in the conn ctor; we set post-handshake so handshake
                    // bytes are unmeasured (small deficit, see TcrConnection.PoolDM
                    // xmldoc).
                    conn.PoolDM = this;
                    var newSize = Interlocked.Increment(ref _poolSize);
                    _stats.PoolConnect();
                    // cppcache :1707-1711 — pool growing past Min means this conn is
                    // "extra" load-conditioning capacity rather than warm-up.
                    if (newSize > xmlPool.MinConnections)
                    {
                        _stats.LoadConditioningConnect();
                    }

                    // Slot ownership transfers to the freshly-opened conn; the
                    // pool-wide release fires at close sites, the per-EP release
                    // rides on conn.DisposeAsync via OwnsEndpointSlot.
                    conn.OwnsEndpointSlot = true;
                    releaseEndpointSlot = false;
                    releaseSlot = false;
                    return conn;
                }
                finally
                {
                    if (releaseEndpointSlot) endpoint.ReleaseSlot();
                }
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
        // Pool-wide MaxConnections cap (cppcache ThinClientPoolDM.cpp:1672-1687) —
        // same AcquirePoolCapSlotAsync helper as CreatePoolConnectionAsync.
        // cppcache signals "cap reached" via a maxConnLimit out-flag so the
        // caller can fall back to a temporary non-pool conn; we throw
        // AllConnectionsInUseException and let the caller catch it.
        await AcquirePoolCapSlotAsync(ct).ConfigureAwait(false);

        var releasePoolSlot = true;
        var releaseEndpointSlot = false;
        try
        {
            // Per-endpoint cap (cppcache TcrEndpoint::m_maxConnections from
            // connection-pool-size, TcrEndpoint.cpp:49-51). Sits beneath the
            // pool-wide cap above. Released by TcrConnection.DisposeAsync via
            // OwnsEndpointSlot once ownership transfers below.
            if (!await endpoint.AcquireSlotAsync(xmlPool.FreeConnectionTimeout, ct).ConfigureAwait(false))
            {
                throw new AllConnectionsInUseException(
                    $"Pool '{xmlPool.Name}' endpoint '{endpoint.Name}': ConnectionPoolSize cap reached.");
            }
            releaseEndpointSlot = true;

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
            // Wire poolDM back-ref so TcrConnection.ReceiveAsync can route
            // #20 ReceivedBytes back to this pool's stats. cppcache sets
            // poolDM_ in the conn ctor; see TcrConnection.PoolDM xmldoc.
            conn.PoolDM = this;
            var newSize = Interlocked.Increment(ref _poolSize);
            _stats.PoolConnect();
            if (newSize > xmlPool.MinConnections)
            {
                _stats.LoadConditioningConnect();
            }

            // Slot ownership transfers to the freshly-opened conn: pool-wide
            // is still released manually by the DM at close sites; per-EP
            // rides along on conn.DisposeAsync via OwnsEndpointSlot.
            conn.OwnsEndpointSlot = true;
            releasePoolSlot = false;
            releaseEndpointSlot = false;
            return conn;
        }
        finally
        {
            if (releasePoolSlot) _capSlots?.Release();
            if (releaseEndpointSlot) endpoint.ReleaseSlot();
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
        // cppcache ThinClientPoolDM::getFromEP (ThinClientPoolDM.cpp:2156-2168):
        // lock + iterate queue_ + return-and-erase first match by
        // getEndpointObject() == theEP. LinkedList<T>'s in-place
        // Remove(node) keeps FIFO for non-matches.
        ct.ThrowIfCancellationRequested();
        lock (_opConnLock)
        {
            for (var node = _opConnections.First; node is not null; node = node.Next)
            {
                if (ReferenceEquals(node.Value.Endpoint, endpoint))
                {
                    _opConnections.Remove(node);
                    logger.LogDebug(
                        "ThinClientPoolDM::getFromEP matched conn for {Endpoint}",
                        endpoint.Name);
                    return Task.FromResult<TcrConnection?>(node.Value);
                }
            }
            return Task.FromResult<TcrConnection?>(null);
        }
    }

    /// <summary>
    /// Return a borrowed <see cref="TcrConnection"/> to the pool queue.
    /// Mirrors cppcache <c>ThinClientPoolDM::put(conn, isTransaction)</c>
    /// (the <c>false</c> overload — sticky-tx routing is Phase 6).
    /// </summary>
    private async ValueTask PutInQueueAsync(TcrConnection conn, CancellationToken ct)
    {
        // Stamp last-access (under lock) so CleanStaleConnectionsAsync
        // sees the same order as the enqueue. destroyed-guard test is
        // deferred — see PROGRESS.md Phase 1.5 "PutInQueueAsync tests".
        // Phase 6 (sticky-tx isTransaction overload, cppcache
        // ThinClientPoolDM.cpp:2293-2300) is the other still-open branch.
        ct.ThrowIfCancellationRequested();
        bool destroyed;
        lock (_opConnLock)
        {
            destroyed = Volatile.Read(ref _isDestroyed) != 0;
            if (!destroyed)
            {
                conn.Touch();
                _opConnections.AddLast(conn);
            }
        }
        if (destroyed)
        {
            // Pool already destroyed (and its queue drained) — close this
            // late-returning conn so its socket / stats don't leak.
            // Mirrors cppcache `put` closed_ branch
            // (ConnectionQueue.hpp:62-67): when the queue refuses the
            // conn, close + delete instead of enqueue.
            await conn.CloseAsync(_keepAlive, ct).ConfigureAwait(false);
        }
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
        logger.LogDebug("Restoring minimum connection level for pool {Pool} (min={Min})", Name, min);

        // cppcache `limit = 2 * min` (L531) caps the retry budget per tick:
        // protects against a race where CreatePoolConnection succeeds but
        // another thread closes the conn before _poolSize catches up,
        // which would otherwise spin the while-loop indefinitely.
        var limit = 2 * min;
        var restored = 0;
        while (Volatile.Read(ref _poolSize) < min && limit-- > 0)
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
            lock (_opConnLock) _opConnections.AddLast(conn);
            restored++;
            _stats.MinPoolSizeConnect();
        }

        int queueSize;
        lock (_opConnLock) queueSize = _opConnections.Count;
        logger.LogDebug(
            "Restored {Restored} connection(s) for pool {Pool}; queue size = {QueueSize}, _poolSize = {PoolSize}",
            restored, Name, queueSize, Volatile.Read(ref _poolSize));
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
    /// (<c>ThinClientPoolDM.cpp:264-371</c>).
    /// </summary>
    private async Task StartBackgroundThreads(CancellationToken ct)
    {
        SchedulePingLoop();

        ScheduleUpdateLocatorLoop();

        _connManageLoop = ConnManageLoopAsync(_backgroundCts.Token);

        await base.InitAsync(ct).ConfigureAwait(false);

        if (xmlPool.PrSingleHopEnabled)
        {
            _clientMetadataService = ActivatorUtilities.CreateInstance<ClientMetadataService>(serviceProvider, this);
            await _clientMetadataService.StartAsync(ct).ConfigureAwait(false);
        }
    }

    public override async Task DestroyAsync(bool keepAlive = false, CancellationToken ct = default)
    {
        // Single override satisfies both ThinClientBaseDM.DestroyAsync
        // (virtual) and IPool.DestroyAsync (interface).
        //
        // Mirror cppcache ThinClientPoolDM::destroy() order
        // (ThinClientPoolDM.cpp:784-848). Inline TODOs flag steps not
        // yet implemented; they sit at their cppcache-equivalent position.
        //   0.  checkRegions (cppcache L787)                          — TODO
        //   1.  mark destroyed (idempotent). Note: we set _isDestroyed
        //       at the top for guard safety; cppcache sets it at end (L842).
        //   1b. close RemoteQueryService (cppcache L791-794)
        //   —   PoolStatsSampler (L796-800): not ported (Meter-based).
        //   2.  cancel background CTS — every loop's Task.Delay /
        //       WaitAsync throws OperationCanceledException
        //       (cppcache L802-810 stopNoblock).
        //   3.  await each background Task so they fully unwind
        //       (cppcache L815 stopPingThread, L818 stopUpdateLocator).
        //   3b. stop ClientMetadataService (cppcache L820-823).
        //   4.  dispose timers + sync primitives (C#-only; cppcache RAII).
        //   5a. drain _opConnections, CloseConnection(18) per conn
        //       (cppcache L829 ConnectionQueue close).
        //   5b. release TCCM endpoint refs                            — TODO Phase 1.5
        //       (ConnManager.RemoveRefToTcrEndpointAsync).
        //   5c. unregister PoolConnections gauge reader. Full
        //       _stats.Close() (cppcache L835 getStats().close())     — TODO
        //       forceSample (L836) — not needed for Meter (pull-based).
        //   5d. PoolManager.RemovePool(name) (cppcache L838)          — TODO
        //   6.  base.DestroyAsync — mirror cppcache stopChunkProcessor
        //       (L840). Caller's ct flows through to base and to per-conn
        //       CloseAsync; background-loop cancellation is separate via
        //       _backgroundCts.
        //   6b. closeAllStickyConnections post-close (cppcache L841).
        //   7.  warn if pool size != 0 (cppcache L846-848)            — TODO

        // 0. TODO: checkRegions — cppcache L787 verifies region consistency
        //    before tearing down (e.g. no pending PR ops on dead buckets).

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

        // 3b. Stop the client metadata service. cppcache
        // ThinClientPoolDM::destroy (L820-823): after loops, before
        // ConnectionQueue close.
        if (_clientMetadataService is not null)
        {
            await _clientMetadataService.StopAsync(ct).ConfigureAwait(false);
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
        //     Snapshot-and-clear under lock so CloseAsync's await isn't
        //     held under the lock (close I/O may be slow).
        List<TcrConnection> drained;
        lock (_opConnLock)
        {
            drained = [.. _opConnections];
            _opConnections.Clear();
        }
        foreach (var conn in drained)
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

        // 5c. Unregister gauge readers so the static registries in
        // PoolStatistics don't leak this pool's entries.
        // TODO: full _stats.Close() to match cppcache getStats().close()
        //   (L835) — drop static-registry entries for every instrument,
        //   not just these gauges. forceSample (L836) is not needed for
        //   Meter (listeners pull on their own cadence).
        _stats.ClearPoolConnectionsReader();
        _stats.ClearLocatorsReader();
        _stats.ClearServersReader();
        _stats.ClearConnectedServersReader();
        _stats.ClearConnectionWaitsInProgressReader();
        _stats.ClearClientOpsInProgressReader();

        // 5d. TODO: PoolManager.RemovePool(name) — cppcache L838
        //     `cacheImpl->getPoolManager().removePool(m_poolName)`
        //     unregisters the pool from cache's registry. Needs a
        //     RemovePool API on our PoolManager first (verify whether
        //     one exists; if not, add it).

        // 6. Mirror cppcache stopChunkProcessor (ThinClientPoolDM.cpp:840) —
        // base.DestroyAsync flips InitDone off and will stop the
        // chunk-processor task when its TODO lands.
        await base.DestroyAsync(keepAlive, ct).ConfigureAwait(false);

        // 6b. closeAllStickyConnections — cppcache L841.
        if (_stickyManager is not null)
        {
            await _stickyManager.CloseAllStickyConnectionsAsync(ct).ConfigureAwait(false);
        }

        // 7. TODO: warn if pool size != 0 — cppcache L846-848 logs FINE
        //    when m_poolSize.load() != 0 after destroy (diagnostic for
        //    leaked conns). One LogWarning when _poolSize > 0.
    }

    public override async Task InitAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, typeof(ThinClientPoolDM));
        if (Interlocked.Exchange(ref _initGuard, 1) != 0) return;

        _stats.SetPoolConnectionsReader(() => Volatile.Read(ref _poolSize));
        _stats.SetServersReader(() => _endpoints.Count);
        _stats.SetConnectedServersReader(() => Volatile.Read(ref _connectedEndpoints));
        _stats.SetConnectionWaitsInProgressReader(() => Volatile.Read(ref _connectionWaitsInProgress));
        _stats.SetClientOpsInProgressReader(() => Volatile.Read(ref _clientOpsInProgress));
        // _locatorHelper is built lazily in ScheduleUpdateLocatorLoop when
        // locators are configured; the reader closes over the field so the
        // gauge starts at 0 and flips to the helper's count once it appears.
        _stats.SetLocatorsReader(() => _locatorHelper?.LocatorCount ?? 0);

        _isMultiUserMode = xmlPool.MultiuserAuthentication ?? false;
        if (_isMultiUserMode)
        {
            logger.LogInformation("Multiuser authentication is enabled for pool {PoolName}", xmlPool.Name);
        }
        _isSecurityOn = options.Security.Properties.Count > 0;
        logger.LogDebug("ThinClientPoolDM.InitAsync: security on/off = {IsSecurityOn}", _isSecurityOn);

        _stickyManager = ActivatorUtilities.CreateInstance<ThinClientStickyManager>(serviceProvider, this);
        _clearPdxRegistry = options.Pdx.ClearTypeIdsOnDisconnect;

        // ── TCCM init — hoisted to Cache.InitializeCoreAsync.

        await StartBackgroundThreads(ct).ConfigureAwait(false);

        // ── Lazy conn opening — first conn opens via ConnManageLoop (RestoreMinConnections) or SendRequestToEndpointAsync.
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
    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrEndpoint endpoint,
        CancellationToken ct = default)
        => SendRequestToEndpointCoreAsync(request, chunkedResult: null, endpoint, ct);

    /// <summary>
    /// Chunked-reply variant of
    /// <see cref="SendRequestToEndpointAsync(TcrMessage, TcrEndpoint, CancellationToken)"/>.
    /// Same conn borrow / put-back shape, only the wire I/O leg differs
    /// (<see cref="TcrConnection.SendRequestAsync(TcrMessage, TcrChunkedResult, CancellationToken)"/>).
    /// </summary>
    public override Task<TcrMessage> SendRequestToEndpointAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        TcrEndpoint endpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunkedResult);
        return SendRequestToEndpointCoreAsync(request, chunkedResult, endpoint, ct);
    }

    /// <summary>
    /// Shared body for both <see cref="SendRequestToEndpointAsync(TcrMessage, TcrEndpoint, CancellationToken)"/>
    /// overloads — borrow / open conn → auth-check → wire I/O → put-back.
    /// </summary>
    /// <remarks>
    /// cppcache <c>ThinClientPoolDM::sendRequestToEP</c> is itself one
    /// function (chunked vs. non-chunked is configured on the reply
    /// object, not by a separate overload). <paramref name="chunkedResult"/>
    /// is the only branch point — non-null routes to the chunked
    /// <c>TcrConnection.SendRequestAsync</c> overload.
    /// </remarks>
    private async Task<TcrMessage> SendRequestToEndpointCoreAsync(
        TcrMessage request,
        TcrChunkedResult? chunkedResult,
        TcrEndpoint endpoint,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpoint);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);

        logger.LogDebug(
            "ThinClientPoolDM::sendRequestToEP{Variant} type={MessageType} endpoint={Endpoint}",
            chunkedResult is null ? "" : " (chunked)",
            request.MessageType,
            endpoint.Name);

        // Step 1 — borrow idle. cppcache: getFromEP(currentEndpoint).
        var conn = await GetFromEPAsync(endpoint, ct).ConfigureAwait(false);

        // Step 2 — open fresh if none idle. cppcache splits pool-cap fallback
        // into a temporary conn with putConnInPool=false; Phase 1.1 collapses
        // both branches (no maxConn limiter yet).
        var putConnInPool = true;
        conn ??= await CreatePoolConnectionToAEndPointAsync(endpoint, ct).ConfigureAwait(false);

        if (conn is null)
        {
            endpoint.SetConnected(false);
            throw new GeodeException(
                $"ThinClientPoolDM: could not obtain a connection to {endpoint.Name}.");
        }

        // Phase 3 — auth / multi-user creds. cppcache ThinClientPoolDM.cpp:1912.
        if ((IsSecurityOn || IsMultiUserMode) && TcrMessage.IsUserInitiativeOps(request))
        {
            throw new NotImplementedException(
                "Phase 3 — SendUserCredentialsAsync (multi-user auth path)");
        }

        try
        {
            // Step 3 — wire I/O. cppcache: sendRequestConnWithRetry; the
            // per-conn retry wrap is Phase 1.5.
            var reply = chunkedResult is null
                ? await conn.SendRequestAsync(request, ct).ConfigureAwait(false)
                : await conn.SendRequestAsync(request, chunkedResult, ct).ConfigureAwait(false);

            // Phase 3 — AuthenticationRequiredException retry. cppcache ThinClientPoolDM.cpp:1975.
            if (IsSecurityOn
                && reply.MessageType == MessageType.Exception
                && IsAuthRequireException(reply.GetException()))
            {
                // Phase 3 step list — mirror ThinClientPoolDM.cpp:1971-1992.
                //   Step A — wrap Steps 1-3 in a retry frame:
                //            `var retriesLeft = 2;` declared once before the
                //            frame, body re-runnable while `retriesLeft >= 0`.
                //   Step B — clear cached auth state on the failing endpoint:
                //              single-user → endpoint.SetAuthenticated(false)
                //              multi-user  → userAttrs.UnauthenticateEP(ep)
                //            (cppcache 1976-1980).
                //   Step C — `retriesLeft--`; on `< 0` rethrow as
                //            GeodeAuthenticationException (we don't mirror
                //            cppcache's reset-to-NOERR + continue — C#
                //            exceptions replace the GfErrType loop).
                //   Step D — return conn to the pool (or dispose), same as
                //            the Step 4 happy path; next iteration re-borrows.
                //   Step E — loop to top of the retry frame; the
                //            IsUserInitiativeOps + IsSecurityOn guard above
                //            will then invoke SendUserCredentialsAsync before
                //            re-sending.
                throw new NotImplementedException(
                    "Phase 3 — auth-required reply: unauth + outer retry loop");
            }

            // Step 4 — happy path. cppcache: putConnInPool ? put(conn, false) : close+delete(conn).
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
        catch (Exception ex)
        {
            // cppcache: setConnectionStatus(false) + removeEPConnections(1)
            // + removeEPFromMetadataIfError. Phase 1.5 will refine via
            // GfErrType classification (retry vs. mark-down).
            endpoint.SetConnected(false);
            if (putConnInPool)
            {
                Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
            }
            await conn.DisposeAsync().ConfigureAwait(false);
            RemoveEPFromMetadataIfError(endpoint, ex);
            throw;
        }
    }


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
    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default)
        => SendSyncRequestCoreAsync(request, chunkedResult: null, attemptFailover, isBackgroundThread, ct);

    /// <summary>
    /// Chunked-reply variant of
    /// <see cref="SendSyncRequestAsync(TcrMessage, bool, bool, CancellationToken)"/>.
    /// </summary>
    public override Task<TcrMessage> SendSyncRequestAsync(
        TcrMessage request,
        TcrChunkedResult chunkedResult,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chunkedResult);
        return SendSyncRequestCoreAsync(request, chunkedResult, attemptFailover, isBackgroundThread, ct);
    }

    /// <summary>
    /// Shared body for both <see cref="SendSyncRequestAsync(TcrMessage, bool, bool, CancellationToken)"/>
    /// overloads — selectEndpoint → addEP → endpoint-pinned send.
    /// </summary>
    private async Task<TcrMessage> SendSyncRequestCoreAsync(
        TcrMessage request,
        TcrChunkedResult? chunkedResult,
        bool attemptFailover,
        bool isBackgroundThread,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDestroyed) != 0, this);

        _ = isBackgroundThread;     // Phase 1.5: sticky flag + stats hook.

        logger.LogDebug(
            "ThinClientPoolDM::sendSyncRequest{Variant} type={MessageType} txId={TxId}",
            chunkedResult is null ? "" : " (chunked)",
            request.MessageType, request.TransactionId);

        // Pool ReadTimeout linked onto caller ct for non-query types.
        // cppcache:1281-1292 (query-family carries its own wire-level timeout).
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!IsQueryFamilyType(request.MessageType))
        {
            linkedCts.CancelAfter(xmlPool.ReadTimeout);
        }
        var effectiveCt = linkedCts.Token;

        // #15 in-progress + #16/#17 timing. cppcache bumps m_clientOps at
        // entry (`:1272`) and decrements + records #16/#17/#18/#19 at every
        // exit (`:1519, 1538`). Stopwatch only records on the success path;
        // outer catch classifies non-success exits into #18 / #19 +
        // re-throws. Caller-cancellation propagates without classification.
        Interlocked.Increment(ref _clientOpsInProgress);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Step A — retry frame state. cppcache:1294-1304.
            // attemptFailover=false pins to a single attempt regardless of
            // pool config (subscription / one-shot callers).
            var retriesLeft = attemptFailover ? xmlPool.RetryAttempts + 1 : 1;
            var retryAllEpsOnce = attemptFailover && xmlPool.RetryAttempts == -1;
            var excludeServers = new HashSet<DnsEndPoint>();
            var firstTry = true;
            Exception? lastError = null;

            // Step B — retry frame. cppcache:1294-1322.
            while (retryAllEpsOnce || retriesLeft-- > 0)
            {
                // Step C — retry bit on resend. cppcache:1309.
                if (!firstTry) request = request.UpdateHeaderForRetry();

                // Step D — query-family timeout doesn't retry. cppcache:1312-1322.
                if (lastError is OperationCanceledException && IsQueryFamilyType(request.MessageType))
                {
                    throw lastError;
                }

                // Hoisted for Step F (catch quarantines the failed location).
                DnsEndPoint? attemptedLocation = null;
                try
                {
                    // Step 1 — pick endpoint. cppcache: selectEndpoint(excludeServers).
                    attemptedLocation = await SelectEndpointAsync(excludeServers, effectiveCt).ConfigureAwait(false);

                    // Step 2 — get-or-create TcrEndpoint (cppcache inlines this in selectEndpoint).
                    var endpoint = await AddEPAsync(attemptedLocation, effectiveCt).ConfigureAwait(false);

                    // Step 3 — endpoint-pinned send.
                    // TODO Phase 1.5 — sticky / isBGThread put-back flag
                    // (cppcache:1427-1436: isBGThread || GET_ALL_70 ||
                    // GET_ALL_WITH_CALLBACK || EXECUTE_REGION_FUNCTION_SINGLE_HOP).
                    // Blocked on StickyManager landing.
                    var reply = chunkedResult is null
                        ? await SendRequestToEndpointAsync(request, endpoint, effectiveCt).ConfigureAwait(false)
                        : await SendRequestToEndpointAsync(request, chunkedResult, endpoint, effectiveCt).ConfigureAwait(false);

                    // TODO Phase 4 — PR single-hop metadata refresh
                    // (cppcache:1484-1508: reply.getMetaDataVersion() +
                    // request.forSingleHop() → EnqueueForMetadataRefresh).

                    // #16 + #17 success record. cppcache:1521, 1541.
                    _stats.ClientOp(stopwatch.Elapsed);
                    return reply;
                }
                catch (Exception ex) when (IsRetryableTransportError(ex, ct))
                {
                    // Step E — transport-error catch (first-cut taxonomy in
                    // IsRetryableTransportError; full GfErrType port deferred).
                    lastError = ex;
                    logger.LogDebug(
                        ex,
                        "ThinClientPoolDM::sendSyncRequest retry-eligible failure (type={MessageType} txId={TxId} endpoint={Endpoint}); attempts left {RetriesLeft}.",
                        request.MessageType, request.TransactionId, attemptedLocation, retriesLeft);

                    // Step F — quarantine the failed endpoint. cppcache:1453.
                    if (attemptedLocation is not null)
                    {
                        excludeServers.Add(attemptedLocation);
                    }
                    firstTry = false;
                }
            }

            // Step G — retries exhausted (cppcache: GfErrType return).
            throw lastError ?? new GeodeException(
                $"Pool '{xmlPool.Name}': all retry attempts exhausted.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-driven cancellation — not an op failure, no #18/#19.
            throw;
        }
        catch (Exception ex)
        {
            // #18 / #19 classify-on-exit. cppcache:1522-1525, 1542-1545.
            if (IsClientOpTimeout(ex)) _stats.ClientOpTimeout();
            else _stats.ClientOpFailure();
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _clientOpsInProgress);
        }
    }

    /// <summary>
    /// First-cut error taxonomy for the DM retry frame
    /// (<see cref="SendSyncRequestCoreAsync"/>): true when
    /// <paramref name="ex"/> is a transport-level failure that warrants
    /// a retry on (eventually) another endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache's full taxonomy is a <c>GfErrType</c> enum classified per
    /// call site (<c>handleEPError</c>, <c>isFatalError</c>, ...); the
    /// fully-faithful port is a separate Phase 1.5 prereq. This first cut
    /// covers IO / socket / timeout exceptions and the
    /// <see cref="OperationCanceledException"/> raised by our
    /// ReadTimeout-linked CTS (distinguished from caller cancellation by
    /// the caller's <see cref="CancellationToken"/> not being cancelled).
    /// </para>
    /// </remarks>
    private static bool IsRetryableTransportError(Exception ex, CancellationToken callerCt) => ex switch
    {
        OperationCanceledException => !callerCt.IsCancellationRequested,
        System.Net.Sockets.SocketException => true,
        IOException => true,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>
    /// True when <paramref name="ex"/> represents a pool-op timeout for
    /// stat classification (cppcache <c>incTimeoutClientOps</c>, #19).
    /// Caller-cancellation is filtered upstream by the outer
    /// <c>catch (OperationCanceledException) when (ct.IsCancellationRequested)</c>,
    /// so any <see cref="OperationCanceledException"/> reaching here was
    /// driven by our linked CTS (ReadTimeout) or a query-family wire
    /// timeout — both timeouts.
    /// </summary>
    private static bool IsClientOpTimeout(Exception ex) =>
        ex is TimeoutException or OperationCanceledException;

    /// <summary>
    /// Record <paramref name="bytes"/> on the <c>ReceivedBytes</c>
    /// Counter (catalogue #20). Called from
    /// <see cref="TcrConnection.ReceiveAsync"/> via the conn's
    /// <see cref="TcrConnection.PoolDM"/> back-ref. Wrapper so
    /// <c>_stats</c> stays encapsulated.
    /// </summary>
    internal void RecordReceivedBytes(long bytes) =>
        _stats.ReceivedBytes(bytes);

    /// <summary>
    /// True for the query / bulk / function message types that cppcache
    /// <c>sendSyncRequest</c> treats specially at
    /// <c>ThinClientPoolDM.cpp:1281-1292, 1312-1322</c>: they carry their
    /// own wire-level <c>messageResponseTimeout</c> part, so the pool
    /// skips its own <c>ReadTimeout</c> stamp and never retries them
    /// after a timeout (the server already gave up).
    /// </summary>
    private static bool IsQueryFamilyType(MessageType type) => type is
        MessageType.Query or
        MessageType.QueryWithParameters or
        MessageType.PutAll or
        MessageType.PutAllWithCallback or
        MessageType.ExecuteFunction or
        MessageType.ExecuteRegionFunction or
        MessageType.ExecuteRegionFunctionSingleHop or
        MessageType.ExecuteCqWithIr;

    public bool IsDestroyed => Volatile.Read(ref _isDestroyed) != 0;

    public string Name => xmlPool.Name;

    public IQueryService QueryService =>
        LazyInitializer.EnsureInitialized(
            ref _queryService,
            () => ActivatorUtilities.CreateInstance<RemoteQueryService>(serviceProvider, this));

    /// <summary>
    /// Test-only: current pool connection count (cppcache <c>m_poolSize</c>).
    /// Bumped in <see cref="CreatePoolConnectionAsync"/> step 4 after a
    /// fresh <see cref="TcrConnection"/> handshakes successfully.
    /// </summary>
    internal int PoolSize => Volatile.Read(ref _poolSize);


    #region Ping

    private Task? _pingLoop;
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
            logger.LogDebug("ThinClientPoolDM::startBackgroundThreads: Scheduling ping task at {Interval}", pingInterval);
            _pingTimer = new PeriodicTimer(pingInterval);
            _pingLoop = PingLoopAsync(_backgroundCts.Token);
        }
        else
        {
            logger.LogDebug("ThinClientPoolDM::startBackgroundThreads: Not scheduling ping task as ping interval {Interval}", pingInterval);
        }
    }

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
        var sweepStopwatch = Stopwatch.StartNew();
        try
        {
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

                var endpointStopwatch = Stopwatch.StartNew();
                await endpoint.PingAsync(this, ct).ConfigureAwait(false);

                if (endpoint.IsConnected)
                {
                    _stats.EndpointPing(endpointStopwatch.Elapsed);
                }
                else
                {
                    // cppcache (ThinClientPoolDM.cpp:2034-2037): ping flipped
                    // endpoint's connected_ bit to false → drop pool's
                    // references on its conns + HA subscription channel.
                    logger.LogDebug("Ping flipped endpoint {Endpoint} to disconnected; cleaning up.", endpoint.Name);
                    await RemoveEPConnectionsAsync(endpoint, ct).ConfigureAwait(false);
                    await RemoveCallbackConnectionAsync(endpoint, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _stats.PingSweep(sweepStopwatch.Elapsed);
        }
    }

    #endregion

    #region Connection Manager

    private Task? _connManageLoop;
    private readonly SemaphoreSlim _connManageSignal = new(0, int.MaxValue);

    /// <summary>
    /// Periodic conn-management loop. Mirrors cppcache
    /// <c>ThinClientPoolDM::manageConnectionsInternal()</c>
    /// (<c>ThinClientPoolDM.cpp:554-575</c>): each tick runs clean-stale,
    /// clean-sticky, restore-min in that order.
    /// </summary>
    private async Task ConnManageLoopAsync(CancellationToken ct)
    {
        // 1s initial delay (cppcache L343-344) pre-opens MinConnections
        // within ~1s so the first user op finds an aged conn in the queue
        // instead of lazy-opening a fresh one (server hasn't finished
        // registering it → RegionDestroyedException on first request).
        var initialDelay = TimeSpan.FromSeconds(1);
        var interval = xmlPool.IdleTimeout;
        try
        {
            await Task.Delay(initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                int queueSize;
                lock (_opConnLock) queueSize = _opConnections.Count;
                logger.LogTrace(
                    "ConnManage tick for pool {Pool}: queue size = {QueueSize}, _poolSize = {PoolSize}",
                    Name, queueSize, Volatile.Read(ref _poolSize));

                try
                {
                    await CleanStaleConnectionsAsync(ct).ConfigureAwait(false);
                    await CleanStickyConnectionsAsync(ct).ConfigureAwait(false);
                    await RestoreMinConnectionsAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // cppcache L568-574 catch-all + LOGERROR: survive one bad tick.
                    logger.LogWarning(ex, "ConnManage tick failed for pool {Pool}", Name);
                }

                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* graceful shutdown */ }
    }

    /// <summary>
    /// One sweep of the idle queue: drop / replace stale connections.
    /// Mirrors cppcache <c>ThinClientPoolDM::cleanStaleConnections</c>
    /// (<c>ThinClientPoolDM.cpp:402-~500</c>). Called once per
    /// <see cref="ConnManageLoopAsync"/> tick before
    /// <see cref="RestoreMinConnectionsAsync"/>.
    /// </summary>
    private async Task CleanStaleConnectionsAsync(CancellationToken ct)
    {
        var idle = xmlPool.IdleTimeout;
        var loadCond = xmlPool.LoadConditioningInterval;
        var min = xmlPool.MinConnections;

        var (removelist, savedConns) = ClassifyStaleConns(idle, loadCond, min, ct);
        await ReplaceOrDeleteStaleConnsAsync(removelist, min - savedConns, loadCond, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Walk the queue once and classify each conn as save (re-queue at tail),
    /// load-conditioning (age &gt; <see cref="CachePoolOptions.LoadConditioningInterval"/>),
    /// or idle-shrink (unused &gt; <see cref="CachePoolOptions.IdleTimeout"/> AND
    /// pool above <see cref="CachePoolOptions.MinConnections"/>). Mirrors cppcache
    /// <c>cleanStaleConnections</c> L412-436.
    /// </summary>
    private (List<(TcrConnection Conn, RemovalReason Reason)> Removelist, int SavedConns)
        ClassifyStaleConns(TimeSpan idle, TimeSpan loadCond, int min, CancellationToken ct)
    {
        // Snapshot-bound the sweep (cppcache `availableConns = size()`):
        // own re-pushes don't re-inspect; other-thread returns wait next tick.
        int snapshot;
        lock (_opConnLock) snapshot = _opConnections.Count;
        var removelist = new List<(TcrConnection Conn, RemovalReason Reason)>();
        var savedConns = 0;

        for (var i = 0; i < snapshot; i++)
        {
            ct.ThrowIfCancellationRequested();

            TcrConnection conn;
            lock (_opConnLock)
            {
                var node = _opConnections.First;
                if (node is null) break;   // drained early (cppcache `getNoWait → nullptr`)
                conn = node.Value;
                _opConnections.RemoveFirst();
            }

            // cppcache canItBeDeleted (L2107-2121): idle threshold falls back to
            // loadCond when shorter / disabled. Reason split so the close site
            // picks the right counter (cppcache lumps both into incLoadCondDisconnects).
            var effectiveIdle = (loadCond > TimeSpan.Zero && (loadCond < idle || idle <= TimeSpan.Zero))
                ? loadCond
                : idle;

            // TODO Phase 2+ HA: skip conns carrying a subscription queue
            //   (cppcache canItBeDeleted L2124-2140). Pool-only mode has no
            //   subscription channel so every conn is eligible.

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
                lock (_opConnLock) _opConnections.AddLast(conn);
                savedConns++;
            }
        }

        return (removelist, savedConns);
    }

    /// <summary>
    /// For each classified-stale conn, either rotate it (open a fresh conn while
    /// the pool still needs to hit <see cref="CachePoolOptions.MinConnections"/>) or
    /// close it outright (pure shrink). Mirrors cppcache
    /// <c>cleanStaleConnections</c> L444-499.
    /// </summary>
    private async Task ReplaceOrDeleteStaleConnsAsync(
        List<(TcrConnection Conn, RemovalReason Reason)> removelist,
        int replaceCount,
        TimeSpan loadCond,
        CancellationToken ct)
    {
        foreach (var (conn, reason) in removelist)
        {
            ct.ThrowIfCancellationRequested();

            if (replaceCount <= 0)
            {
                // Pure shrink — savedConns covers Min, close without replacement.
                await SafeCloseAsync(conn, "pure-shrink").ConfigureAwait(false);
                Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                switch (reason)
                {
                    case RemovalReason.LoadConditioning: _stats.LoadConditioningDisconnect(); break;
                    case RemovalReason.Idle: _stats.IdleDisconnect(); break;
                }
            }
            else
            {
                // Pass `conn` as currentServer hint so SelectEndpoint can return
                // the same endpoint and CreatePoolConnectionAsync recycles the conn
                // without re-handshaking (cppcache L455-459).
                var newConn = await CreatePoolConnectionAsync(
                    [], currentServer: conn, ct).ConfigureAwait(false);
                if (newConn is not null)
                {
                    lock (_opConnLock) _opConnections.AddLast(newConn);
                    // newConn == conn means recycle; only close on real swap.
                    if (!ReferenceEquals(newConn, conn))
                    {
                        await SafeCloseAsync(conn, "swap").ConfigureAwait(false);
                        Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                        _stats.LoadConditioningDisconnect();
                        _stats.LoadConditioningConnect();
                    }
                }
                else if (conn.HasExpired(loadCond))
                {
                    // Replacement failed AND past loadCond → close anyway (doomed).
                    await SafeCloseAsync(conn, "doomed-expired").ConfigureAwait(false);
                    Interlocked.Decrement(ref _poolSize); _capSlots?.Release();
                    _stats.LoadConditioningDisconnect();
                }
                else
                {
                    // Replacement failed, not expired → reset age + push back
                    // (cppcache L488); else re-elected every sweep.
                    conn.UpdateCreationTime();
                    lock (_opConnLock) _opConnections.AddLast(conn);
                }
                replaceCount--;
            }
        }

        // One bad CloseAsync must not abort the sweep (cppcache uses destructor-safe
        // `try { GF_SAFE_DELETE } catch (...) {}`). keepAlive:false — transient cleanup.
        async ValueTask SafeCloseAsync(TcrConnection c, string context)
        {
            try { await c.CloseAsync(keepAlive: false, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "CloseAsync threw during CleanStale ({Context}); continuing.", context);
            }
        }
    }

    private enum RemovalReason { LoadConditioning, Idle }

    /// <summary>
    /// Per-tick sticky-conn cleanup hook. Mirrors cppcache
    /// <c>ThinClientPoolDM::cleanStickyConnections</c>
    /// (<c>ThinClientPoolDM.cpp:521</c>) — base body is empty <c>{}</c>;
    /// <see cref="ThinClientPoolStickyDM"/> overrides to dispatch into
    /// <see cref="ThinClientStickyManager.CleanStaleStickyConnectionAsync"/>.
    /// </summary>
    protected virtual Task CleanStickyConnectionsAsync(CancellationToken ct)
    {
        _ = ct;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Close every conn in <see cref="_opConnections"/> that belongs to
    /// <paramref name="endpoint"/>. Mirrors cppcache
    /// <c>ThinClientPoolDM::removeEPConnections(TcrEndpoint*)</c>
    /// (<c>ThinClientPoolDM.cpp:2170-2188</c>) — called when ping flips
    /// the endpoint to disconnected, so the pool stops handing out its
    /// stale conns.
    /// </summary>
    private async Task RemoveEPConnectionsAsync(TcrEndpoint endpoint, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Phase 1: scan + remove matching conns under lock (sync, snapshot).
        var removed = new List<TcrConnection>();
        lock (_opConnLock)
        {
            var node = _opConnections.First;
            while (node is not null)
            {
                var next = node.Next;
                if (ReferenceEquals(node.Value.Endpoint, endpoint))
                {
                    _opConnections.Remove(node);
                    removed.Add(node.Value);
                }
                node = next;
            }
        }

        // Phase 2: close each outside the lock — CloseAsync is wire I/O.
        // Pass keepAlive:false (transient cleanup, not pool-wide destroy).
        foreach (var conn in removed)
        {
            await conn.CloseAsync(keepAlive: false, ct).ConfigureAwait(false);
            Interlocked.Decrement(ref _poolSize);
            _capSlots?.Release();
            _stats.PoolDisconnect();
        }

        // cppcache also `conn_semaphore_.release()`s the manage thread so it
        // can re-fill MinConnections. Our ConnManageLoopAsync uses
        // PeriodicTimer + fires on its next IdleTimeout tick — no explicit
        // wake needed.

        if (removed.Count > 0)
        {
            logger.LogDebug(
                "Removed {Count} conn(s) for endpoint {Endpoint} from pool {Pool}.",
                removed.Count, endpoint.Name, Name);
        }
    }

    /// <summary>
    /// Evict any cached bucket → server mapping pointing at
    /// <paramref name="endpoint"/> when the failure that just dropped a
    /// conn was a transport-level IO / timeout. Mirrors cppcache
    /// <c>ThinClientPoolDM::removeEPFromMetadataIfError</c>
    /// (<c>ThinClientPoolDM.cpp:1555</c>), which gates on
    /// <c>GF_IOERR || GF_TIMEOUT</c> plus a non-null metadata service.
    /// </summary>
    private void RemoveEPFromMetadataIfError(TcrEndpoint endpoint, Exception error)
    {
        // cppcache filters on GfErrType — translate via .NET exception type.
        // Phase 1.5 GfErrType taxonomy will refine this once timeout +
        // partial-write paths surface a richer set of exceptions.
        if (error is not (IOException or TimeoutException)) return;

        _clientMetadataService?.RemoveBucketServerLocation(endpoint.Name);
    }

    /// <summary>
    /// HA subscription channel cleanup for <paramref name="endpoint"/>.
    /// Mirrors cppcache <c>ThinClientPoolDM::removeCallbackConnection</c>
    /// (<c>ThinClientPoolDM.hpp:281</c>) — base body is empty <c>{}</c>;
    /// <see cref="ThinClientPoolHADM"/> overrides to dispatch into the
    /// HA-pool's <c>redundancyManager_</c>.
    /// </summary>
    protected virtual Task RemoveCallbackConnectionAsync(TcrEndpoint endpoint, CancellationToken ct)
    {
        _ = endpoint;
        _ = ct;
        return Task.CompletedTask;
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
        // Options layer surfaces the resolved default (3) directly, so no
        // cppcache-style sentinel translation needed here.
        _locatorHelper = ActivatorUtilities.CreateInstance<ThinClientLocatorHelper>(
            serviceProvider, initialLocators, xmlPool.RetryAttempts);

        var updateInterval = xmlPool.UpdateLocatorListInterval;
        if (updateInterval <= TimeSpan.Zero)
        {
            logger.LogDebug("ThinClientPoolDM::startBackgroundThreads: Not scheduling updateLocatorList as interval {Interval}", updateInterval);
            return;
        }

        logger.LogDebug("ThinClientPoolDM::startBackgroundThreads: Scheduling updateLocatorList task at {Interval}", updateInterval);
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
    private async Task<DnsEndPoint> SelectEndpointFromLocatorAsync(HashSet<DnsEndPoint> excludeServers, CancellationToken ct)
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




    /* todo

    // ── Sticky transactions (Phase 6) ──
    private bool _isSticky;                                       // m_sticky

    // ── State flags (ThinClientPoolDM.hpp:203-205) ──
    private int _destroyPending;                                  // m_destroyPending (Interlocked 0/1)
    private int _destroyPendingHADM;                              // m_destroyPendingHADM (Interlocked 0/1, Phase 2+ HA-pool)

    // ── Identity ──
    // cppcache m_memId is pool-scoped (one ID per pool); we currently build one
    // per TcrConnection via ClientProxyMembershipIdBuilder. Field reserved for
    // the cppcache-faithful pool-shared identity decision (server-side dedup
    // risk needs verification before adopting).
    private ClientProxyMembershipID? _memId;                      // m_memId

    // ── Counters (PoolStatistics catalogue progression) ──
    private int _numRegions;                                      // m_numRegions (region ref count, Interlocked)
    private int _clientOps;                                       // m_clientOps (clientOpsInProgress gauge, Interlocked)
    private int _connectedEndpoints;                              // connected_endpoints_ (Interlocked)

    // ── HA subscription (Phase 2+) ──
    private int _primaryServerQueueSize = -1;                     // m_primaryServerQueueSize (PRIMARY_QUEUE_NOT_AVAILABLE = -1)
*/
}
