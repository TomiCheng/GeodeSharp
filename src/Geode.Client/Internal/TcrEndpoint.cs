using System.Net;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

/// <summary>
/// Single Geode server endpoint. Owns this server's per-endpoint
/// connection pool, the dedicated subscription channel (non-pool /
/// HA only), authentication token, and health flags. Mirrors cppcache
/// <c>TcrEndpoint</c>
/// (<c>cppcache/src/TcrEndpoint.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// One instance per <c>host:port</c>; held in
/// <see cref="TcrConnectionManager"/>'s endpoint registry. Created on
/// first reference (cppcache <c>TcrConnectionManager::addRefToTcrEndpoint</c>).
/// </para>
/// <para>
/// MVP (pool mode) only needs: per-endpoint connection pool,
/// <c>connected_</c> flag, <c>m_uniqueId</c> auth token, and
/// <c>send</c> / <c>createNewConnection</c> / <c>pingServer</c>.
/// Subscription channel + redundancy + multi-user auth + HA queue
/// state are all Phase 2+.
/// </para>
/// <para>
/// Inheritance: base for the cppcache <c>TcrEndpoint</c> /
/// <c>TcrPoolEndPoint</c> split. Pool-mode endpoints should be
/// instantiated as <see cref="TcrPoolEndPoint"/>; this base is reserved
/// for non-pool / legacy paths (deferred per memory
/// <c>pool-only-no-non-pool.md</c>). Migration of pool-only state
/// (per-pool DM ref, <c>getPoolHADM</c>-style accessor) to the
/// subclass is in progress — see PORTING.md.
/// </para>
/// </remarks>
internal class TcrEndpoint(
    IServiceProvider serviceProvider,
    ILogger<TcrEndpoint> logger,
    DnsEndPoint endpoint,
    TcrConnectionManager connectionManager) //: IAsyncDisposable
{
    /// <summary>
    /// Owning <see cref="TcrConnectionManager"/>. Mirrors cppcache's
    /// <c>TcrEndpoint::m_cacheImpl-&gt;tcrConnectionManager()</c>
    /// indirection (we collapse the hop because TCCM is the layer that
    /// actually owns this endpoint registry).
    /// </summary>
    internal TcrConnectionManager ConnectionManager => connectionManager;

    /// <summary>
    /// connected_ (atomic<bool> → Interlocked 0/1)
    /// </summary>
    private int _connected;
    private int _numRegions;
    //    private int _disposed;

    /// <summary>
    /// DMs that have registered interest in this endpoint via
    /// <see cref="RegisterDMAsync"/>. Mirrors cppcache <c>m_distMgrs</c>
    /// (<c>TcrEndpoint.hpp:213</c>). Used as the broadcast list for
    /// endpoint-wide state transitions (e.g. <see cref="SetConnected"/>
    /// fans out <c>Inc/DecConnectedEndpoints</c> to every DM here);
    /// cppcache simplifies by notifying only <c>m_baseDM</c>, but our
    /// list-walk handles multi-pool endpoint sharing correctly. All
    /// access guarded by <see cref="_distMgrsLock"/>.
    /// </summary>
    private readonly List<ThinClientBaseDM> _distMgrs = [];

    /// <summary>
    /// Guards <see cref="_distMgrs"/>. Mirrors cppcache
    /// <c>m_distMgrsLock</c> (<c>TcrEndpoint.hpp:215</c>). Held during
    /// register / unregister and during transition broadcasts so a DM
    /// can't be dropped mid-iteration.
    /// </summary>
    private readonly Lock _distMgrsLock = new();

    //    /// <summary>
    //    /// cppcache <c>m_maxConnections</c> — per-endpoint conn cap from
    //    /// <see cref="PoolOptions.ConnectionPoolSize"/>. <c>0</c> = unlimited
    //    /// (our re-interpretation; cppcache's <c>0</c> is a separate "lazy
    //    /// single conn" mode we don't port).
    //    /// </summary>
    //    private readonly int _maxConnections = cacheScopeContext.Options.Pool.ConnectionPoolSize;

    private bool _msgSent;

    private readonly SemaphoreSlim _notificationCleanupSignal = new(0, int.MaxValue);
    private bool _pingSent;
    private int _pingTimeouts;

    /// <summary>
    /// Slot semaphore enforcing <see cref="_maxConnections"/>. Null when
    /// <see cref="_maxConnections"/> is <c>0</c> (unlimited).
    /// </summary>
    private readonly SemaphoreSlim? _slots = MakeSlotSemaphore(5); // todo

    private static SemaphoreSlim? MakeSlotSemaphore(int size) =>
        size > 0 ? new SemaphoreSlim(size, size) : null;

    /// <summary>
    /// Reserve one of this endpoint's <see cref="_maxConnections"/> slots,
    /// waiting up to <paramref name="timeout"/>. Returns <c>false</c> if
    /// the cap is hit and the wait expires; <c>true</c> when a slot is
    /// acquired (caller must <see cref="ReleaseSlot"/> on conn close) or
    /// the endpoint is in unlimited mode.
    /// </summary>
    internal async ValueTask<bool> AcquireSlotAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_slots is null) return true;
        return await _slots.WaitAsync(timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically increment the region / DM reference count. Mirrors
    /// cppcache <c>setNumRegions(numRegions() + 1)</c> performed inside
    /// <c>TcrConnectionManager::addRefToTcrEndpoint</c>; we hoist the
    /// +1 into a dedicated method so the bump is atomic without
    /// holding the map lock.
    /// </summary>
    /// <returns>The new reference count.</returns>
    internal int IncrementNumRegions() => Interlocked.Increment(ref _numRegions);

    /// <summary>
    /// Release a slot reserved via <see cref="AcquireSlotAsync"/>.
    /// </summary>
    internal void ReleaseSlot() => _slots?.Release();

    //    /// <summary>
    //    /// Run the auth handshake on a freshly-opened connection. Mirrors
    //    /// cppcache <c>TcrEndpoint::authenticateEndpoint</c>.
    //    /// </summary>
    //    public Task AuthenticateEndpointAsync(object connection, CancellationToken ct = default)
    //    {
    //        // TODO Phase 3 (security): send credentials, read uniqueId.
    //        throw new NotImplementedException("TODO: TcrEndpoint.AuthenticateEndpointAsync");
    //    }

    /// <summary>
    /// Open a fresh TCP/TLS connection and run the handshake. Mirrors
    /// cppcache <c>TcrEndpoint::createNewConnection</c>. Linux-only
    /// retry-under-lock variant <c>createNewConnectionWL</c> is bucket
    /// 1 (modern .NET sockets don't need it).
    /// </summary>
    public async Task<TcrConnection> CreateNewConnectionAsync(
        ThinClientPoolDM pool,
        bool isClientNotification,
        bool isSecondary,
        TimeSpan? connectTimeout = null,
        CancellationToken ct = default)
    {
        if (isClientNotification)
        {
            // cppcache: HandShake.cpp builds a different wire format for
            // notification channels (port list, no read-timeout). Our
            // TcrConnection.HandshakeAsync still throws NIE on that branch
            // (Phase 2+ subscription / CQ).
            throw new NotImplementedException("TODO Phase 2+: notification-channel handshake.");
        }
        _ = isSecondary;     // only meaningful with isClientNotification.

        ct.ThrowIfCancellationRequested();

        logger.LogDebug("TcrEndpoint.CreateNewConnection: opening request/response connection to {Host}:{Port}",
            endpoint.Host, endpoint.Port);

        // Pull TcrConnection through DI so its own deps (ILogger<TcrConnection>,
        // IOptions<GeodeClientOptions>, ClientProxyMembershipIdBuilder)
        // resolve cleanly; `this` (Endpoint) + `pool` ride as positional
        // args so the conn carries both its target server identity and
        // its owning pool from ctor onwards.
        var conn = ActivatorUtilities.CreateInstance<TcrConnection>(serviceProvider, this, pool);

        try
        {
            // ConnectAsync bundles TCP connect (Nagle off) + the full
            // client/server handshake (steps 1-14). Mirrors cppcache
            // initTcrConnection: success or throw, no half-states.
            //   • GeodeException — server refused the handshake (REPLY_OK
            //     not received) or pointed at a locator port.
            //   • SocketException / IOException — TCP failure.
            //   • OperationCanceledException — ct cancelled.
            await conn.ConnectAsync(endpoint.Host, endpoint.Port, connectTimeout, ct).ConfigureAwait(false);

            // Endpoint state flags are caller-driven (mirror cppcache):
            //   • SetConnected — ThinClientPoolDM::createPoolConnection
            //     (pool path) / TcrEndpoint::pingServer (probe path).
            //   • _isAuthenticated — set by authenticateEndpoint in
            //     Phase 3 (security mode != NONE). NONE leaves it false.
            return conn;
        }
        catch
        {
            // Don't leak a half-opened conn. cppcache: _GEODE_SAFE_DELETE(newConn)
            // at the bottom of createNewConnection when err != GF_NOERR.
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    //    public ValueTask DisposeAsync()
    //    {
    //        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

    //        _connectLock.Dispose();
    //        _notificationCleanupSignal.Dispose();
    //        _slots?.Dispose();

    //        // TODO: close _opConnections, _notifyConnection, await
    //        //       _notifyReceiver task; release endpoint resources.
    //        return ValueTask.CompletedTask;
    //    }

    /// <summary>
    /// Send <c>MessageType.Ping</c> through <paramref name="poolDM"/> and
    /// update <see cref="IsConnected"/> based on the reply. Mirrors cppcache
    /// <c>TcrEndpoint::pingServer(ThinClientPoolDM*)</c>
    /// (<c>cppcache/src/TcrEndpoint.cpp:499-544</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// cppcache passes <c>poolDM == nullptr</c> for non-pool / standalone
    /// endpoints and falls back to <c>this->send(pingMsg, reply)</c>; we
    /// only run pool-mode in MVP so the null branch throws NIE for now.
    /// </para>
    /// <para>
    /// The cppcache <c>m_msgSent</c> / <c>m_pingSent</c> short-circuit
    /// (<c>TcrEndpoint.cpp:506,540-543</c>) is preserved verbatim — every
    /// other tick is a no-op when there has been recent activity, halving
    /// ping bandwidth when the channel is busy. Phase 1.2's
    /// <c>SendSyncRequestAsync</c> will set <c>_msgSent = true</c> after
    /// each real op so this skip starts paying off; until then
    /// <c>_msgSent</c> stays false and only <c>_pingSent</c> gates the
    /// skip (effective ping cadence = every 2 ticks).
    /// </para>
    /// <para>
    /// cppcache's <c>GF_TIMEOUT</c> tolerance (<c>m_pingTimeouts &lt; 2</c>
    /// before flipping <c>connected_</c>) is deferred to Phase 1.5 — needs
    /// the GfErrType taxonomy to land first so we can distinguish
    /// "transport timed out" from "server returned exception" cleanly. For
    /// now any send-path exception flips connected to false.
    /// </para>
    /// </remarks>
    public async Task PingAsync(
        ThinClientPoolDM? poolDM = null,
        CancellationToken ct = default)
    {
        logger.LogDebug("Sending ping message to endpoint {Endpoint}", Name);

        if (!IsConnected)
        {
            logger.LogTrace("Skipping ping task for disconnected endpoint {Endpoint}", Name);
            return;
        }

        // Activity short-circuit (cppcache L506,540-543).
        if (_msgSent || _pingSent)
        {
            _msgSent = false;
            _pingSent = false;
            return;
        }

        if (poolDM is null)
        {
            // cppcache TcrEndpoint.cpp:514-516 falls back to this->send(...).
            // Standalone / non-pool DM is Phase 2+.
            throw new NotImplementedException("TODO Phase 2+: standalone endpoint.send(ping) path (non-pool DM).");
        }

        var messageBuilder = TcrMessageBuilder.Create(serviceProvider, MessageType.Ping);
        var pingRequest = await messageBuilder.BuildAsync(ct);

        logger.LogTrace("Sending ping message to endpoint {Endpoint}", Name);

        TcrMessage reply;
        try
        {
            reply = await poolDM
                .SendRequestToEndpointAsync(pingRequest, this, ct)
                .ConfigureAwait(false);
            _pingSent = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-driven shutdown — propagate; loop layer treats as graceful.
            throw;
        }
        catch (Exception ex)
        {
            // TODO Phase 1.5: classify as GF_TIMEOUT and tolerate up to 2
            //   consecutive timeouts (++_pingTimeouts) before flipping
            //   connected. cppcache TcrEndpoint.cpp:522-524.
            //   Currently any error flips connected immediately.
            _pingTimeouts = 0;
            logger.LogWarning(ex, "Ping to endpoint {Endpoint} failed; marking disconnected", Name);
            if (IsConnected)
            {
                SetConnected(false);
            }
            return;
        }

        // Non-timeout outcome → reset tolerance counter (cppcache L525).
        _pingTimeouts = 0;

        // cppcache (TcrEndpoint.cpp:532-534): connected iff the server
        // returned a proper Reply frame. Anything else (Exception reply,
        // unexpected MessageType) means the server is unhappy with us.
        var connected = reply.MessageType == MessageType.Reply;
        if (IsConnected != connected)
        {
            SetConnected(connected);
        }

        logger.LogTrace("Completed sending ping message to endpoint {Endpoint} (replyType={ReplyType})",
            Name, reply.MessageType);
    }

    //    /// <summary>
    //    /// Receiver loop body for the subscription channel; mirrors
    //    /// cppcache <c>TcrEndpoint::receiveNotification</c>. Drives event
    //    /// dispatch to registered listeners.
    //    /// </summary>
    //    public Task ReceiveNotificationsAsync(CancellationToken ct = default)
    //    {
    //        // TODO Phase 2+: blocking read on _notifyConnection, decode
    //        //   message, dispatch to ThinClientRegion listeners. Phase 2+.
    //        throw new NotImplementedException("TODO: TcrEndpoint.ReceiveNotificationsAsync");
    //    }

    /// <summary>
    /// Register a DM as a user of this endpoint; opens the dedicated
    /// subscription connection if <paramref name="clientNotification"/>
    /// and not already running. Mirrors cppcache
    /// <c>TcrEndpoint::registerDM</c>.
    /// </summary>
    /// <remarks>
    /// cppcache bundles three concerns; we implement them per phase:
    /// (1) bind dm into <c>_distMgrs</c> &#x2014; Phase 1.1 (used by
    /// Phase 1.5's failover broadcast: a dying endpoint signals every
    /// DM in this list to re-route);
    /// (2) open notification connection + receiver Task &#x2014;
    /// Phase 2+ (subscription / CQ / register-interest);
    /// (3) flip <c>_isActiveEndpoint</c> for redundancy manager &#x2014;
    /// Phase 2+ (HA).
    /// </remarks>
    public Task<int> RegisterDMAsync(
            bool clientNotification,
            bool isSecondary,
            bool isActiveEndpoint,
            ThinClientBaseDM? distributionManager = null,
            CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (clientNotification)
        {
            throw new NotImplementedException(
                "TODO Phase 2+: subscription / notification channel.");
        }
        if (isActiveEndpoint)
        {
            throw new NotImplementedException(
                "TODO Phase 2+: redundancy / active endpoint flag.");
        }
        _ = isSecondary;   // only meaningful when clientNotification.

        if (distributionManager is null)
        {
            return Task.FromResult(0);
        }

        // Dedupe under the lock so repeated AddRefToTcrEndpoint calls
        // from the same pool don't multiply the broadcast list.
        lock (_distMgrsLock)
        {
            if (!_distMgrs.Contains(distributionManager))
            {
                _distMgrs.Add(distributionManager);
            }
        }

        return Task.FromResult(0);
    }

    //    /// <summary>
    //    /// Send a request and wait for reply, choosing a connection from
    //    /// <c>_opConnections</c>. Mirrors cppcache
    //    /// <c>TcrEndpoint::send(request, reply)</c>.
    //    /// </summary>
    //    public Task<int /*GfErrType* /> SendAsync(
    //        object request,                   // TcrMessage
    //        object reply,                     // TcrMessageReply
    //        CancellationToken ct = default)
    //    {
    //        // TODO: dequeue from _opConnections; conn.Send(request, reply);
    //        //       enqueue back; on error → CloseFailedConnection +
    //        //       set _connected = 0 + signal _failoverSignal.
    //        throw new NotImplementedException("TODO: TcrEndpoint.SendAsync");
    //    }

    //    /// <summary>
    //    /// Send with retries against this endpoint's pool. Mirrors cppcache
    //    /// <c>TcrEndpoint::sendRequestWithRetry</c>.
    //    /// </summary>
    //    public Task<int /*GfErrType* /> SendRequestWithRetryAsync(
    //        object request,
    //        object reply,
    //        int maxSendRetries,
    //        CancellationToken ct = default)
    //    {
    //        throw new NotImplementedException("TODO: TcrEndpoint.SendRequestWithRetryAsync");
    //    }

    /// <summary>
    /// Flip <see cref="IsConnected"/> and, on a real 0&#x2194;1 transition,
    /// broadcast <c>Inc/DecConnectedEndpoints</c> to every DM in
    /// <see cref="_distMgrs"/>. Mirrors cppcache
    /// <c>TcrEndpoint::setConnected</c> / <c>setConnectionStatus</c>
    /// (<c>TcrEndpoint.cpp:1114-1123</c>) — cppcache uses
    /// <c>compare_exchange_strong</c> to gate the inc/dec on a real flip;
    /// we use <see cref="Interlocked.CompareExchange(ref int,int,int)"/>
    /// for the same effect. Same-value writes are silent no-ops.
    /// </summary>
    /// <remarks>
    /// Divergence from cppcache: cppcache notifies a single <c>m_baseDM</c>;
    /// we walk <see cref="_distMgrs"/> so multi-pool endpoint sharing
    /// (legal in our TCCM design) sees the transition on every interested
    /// DM. Callees (<see cref="ThinClientBaseDM.IncConnectedEndpoints"/> /
    /// <see cref="ThinClientBaseDM.DecConnectedEndpoints"/>) must stay
    /// lock-free and non-reentrant w.r.t. this endpoint — they run under
    /// <see cref="_distMgrsLock"/>.
    /// </remarks>
    public void SetConnected(bool connected)
    {
        var newVal = connected ? 1 : 0;
        var oldVal = connected ? 0 : 1;
        if (Interlocked.CompareExchange(ref _connected, newVal, oldVal) != oldVal)
        {
            // Same-value write, or another thread won the flip race.
            return;
        }
        lock (_distMgrsLock)
        {
            foreach (var dm in _distMgrs)
            {
                if (connected) dm.IncConnectedEndpoints();
                else dm.DecConnectedEndpoints();
            }
        }
    }

    //    /// <summary>
    //    /// Drop a DM. Mirrors cppcache <c>TcrEndpoint::unregisterDM</c>.
    //    /// When the last DM leaves and notification was started, close
    //    /// the subscription connection.
    //    /// </summary>
    //    public Task UnregisterDMAsync(
    //        bool clientNotification,
    //        object? distributionManager = null,
    //        CancellationToken ct = default)
    //    {
    //        // TODO: drop dm from _distMgrs; if last + clientNotification,
    //        //       stopNotifyReceiverAndCleanup.
    //        throw new NotImplementedException("TODO: TcrEndpoint.UnregisterDMAsync");
    //    }

    //    public DnsEndPoint Endpoint => endpoint;

    //    public bool IsAuthenticated => _isAuthenticated;

    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    /// <summary>Canonical <c>"host:port"</c> rendering for logs / registry keys.</summary>
    public string Name => $"{endpoint.Host}:{endpoint.Port}";

    public int NumRegions
    {
        get => Volatile.Read(ref _numRegions);
        set => Volatile.Write(ref _numRegions, value);
    }

    //    public long UniqueId => Interlocked.Read(ref _uniqueId);

    //#pragma warning disable CS0169, CS0414, CS0649 // placeholder fields mirroring TcrEndpoint; wired up phase by phase

    //    // ── Per-endpoint connection pool (TcrEndpoint.hpp:185-188) ──
    //    private object? _opConnections;       // m_opConnections (ConnectionQueue<TcrConnection>)


    //    private bool _needToConnectInLock;    // m_needToConnectInLock
    //    private bool _connCreatedWhenMaxConnsIsZero; // m_connCreatedWhenMaxConnsIsZero



    //    // ── Subscription channel (Phase 2+; TcrEndpoint.hpp:178-184) ──
    //    private object? _notifyConnection;        // m_notifyConnection (TcrConnection*)
    //    private Task? _notifyReceiver;            // m_notifyReceiver (Task<TcrEndpoint>)
    //    private readonly List<object?> _notifyReceiverList = [];    // m_notifyReceiverList
    //    private readonly List<object?> _notifyConnectionList = [];  // m_notifyConnectionList

    //    // ── DM registration (TcrEndpoint.hpp:211-216) ──
    //    // Pool mode (option B in design notes) routes DMs through _distMgrs
    //    // only — m_baseDM stays unused. Non-pool mode (Phase 2+) may revive
    //    // m_baseDM as a back-pointer to the owning region's DM.
    //    private object? _baseDM;              // m_baseDM (ThinClientBaseDM*) — non-pool only

    //    private readonly Lock _connectionLock = new();
    //    private readonly SemaphoreSlim _connectLock = new(1, 1);       // m_connectLock (timed_mutex; .NET uses await with timeout)
    //    private readonly Lock _notifyReceiverLock = new();
    //    private readonly Lock _endpointAuthenticationLock = new();

    //    // ── Health (TcrEndpoint.hpp:219-228) ──




    //    // ── Auth (TcrEndpoint.hpp:207, 224, 227) ──
    //    private bool _isAuthenticated;        // m_isAuthenticated
    //    private long _uniqueId;               // m_uniqueId (server-issued auth token, set after handshake)
    //    private bool _isMultiUserMode;        // m_isMultiUserMode (Phase 3)

    //    // ── HA / queue state (TcrEndpoint.hpp:189, 229-234) ──
    //    private bool _isQueueHosted;          // m_isQueueHosted
    //    private bool _isActiveEndpoint;       // m_isActiveEndpoint
    //    private int _serverQueueStatus;       // m_serverQueueStatus (enum ServerQueueStatus)
    //    private int _queueSize;               // m_queueSize
    //    private bool _isServerQueueStatusSet; // m_isServerQueueStatusSet
    //    private ushort _distributedMemId;     // m_distributedMemId

    //    // ── Counters (TcrEndpoint.hpp:187, 220-223) ──
    //    private int _numRegionListener;       // m_numRegionListener
    //    private int _numRegions;              // m_numRegions
    //    private int _notifyCount;             // m_notifyCount
    //    private uint _dupCount;               // m_dupCount

    //    // ── TCCM coordination semaphores (TcrEndpoint.hpp:208-210, 217) ──
    //    // cppcache passes binary_semaphore& from TCCM into the endpoint ctor;
    //    // .NET takes them as ctor refs (or via DI) when TCCM truly drives them.
    //    private SemaphoreSlim? _failoverSignal;            // failover_semaphore_
    //    private SemaphoreSlim? _cleanupSignal;             // cleanup_semaphore_
    //    private SemaphoreSlim? _redundancySignal;          // redundancy_semaphore_




    //#pragma warning restore CS0169, CS0414, CS0649


}
