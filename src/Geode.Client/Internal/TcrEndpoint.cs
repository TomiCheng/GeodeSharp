using System.Net;
using Geode.Client.Options;
using Geode.Client.Protocol;
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
/// </remarks>
internal sealed class TcrEndpoint(
    DnsEndPoint endpoint,
    IServiceProvider serviceProvider,
    ILogger<TcrEndpoint> logger) : IAsyncDisposable
{

#pragma warning disable CS0169, CS0414, CS0649 // placeholder fields mirroring TcrEndpoint; wired up phase by phase

    // ── Per-endpoint connection pool (TcrEndpoint.hpp:185-188) ──
    private object? _opConnections;       // m_opConnections (ConnectionQueue<TcrConnection>)
    private int _maxConnections;          // m_maxConnections (from connection-pool-size, per-endpoint)
    private bool _needToConnectInLock;    // m_needToConnectInLock
    private bool _connCreatedWhenMaxConnsIsZero; // m_connCreatedWhenMaxConnsIsZero

    // ── Subscription channel (Phase 2+; TcrEndpoint.hpp:178-184) ──
    private object? _notifyConnection;        // m_notifyConnection (TcrConnection*)
    private Task? _notifyReceiver;            // m_notifyReceiver (Task<TcrEndpoint>)
    private readonly List<object?> _notifyReceiverList = new();    // m_notifyReceiverList
    private readonly List<object?> _notifyConnectionList = new();  // m_notifyConnectionList

    // ── DM registration (TcrEndpoint.hpp:211-216) ──
    // Pool mode (option B in design notes) routes DMs through _distMgrs
    // only — m_baseDM stays unused. Non-pool mode (Phase 2+) may revive
    // m_baseDM as a back-pointer to the owning region's DM.
    private object? _baseDM;              // m_baseDM (ThinClientBaseDM*) — non-pool only
    private readonly List<ThinClientBaseDM> _distMgrs = new();    // m_distMgrs
    // m_distMgrsLock / m_connectionLock / m_connectLock / m_notifyReceiverLock /
    //   m_endpointAuthenticationLock — collapsed where possible:
    private readonly Lock _distMgrsLock = new();
    private readonly Lock _connectionLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);       // m_connectLock (timed_mutex; .NET uses await with timeout)
    private readonly Lock _notifyReceiverLock = new();
    private readonly Lock _endpointAuthenticationLock = new();

    // ── Health (TcrEndpoint.hpp:219-228) ──
    private int _connected;               // connected_ (atomic<bool> → Interlocked 0/1)
    private int _numberOfTimesFailed;     // m_numberOfTimesFailed
    private int _pingTimeouts;            // m_pingTimeouts
    private bool _msgSent;                // m_msgSent (volatile)
    private bool _pingSent;               // m_pingSent (volatile)

    // ── Auth (TcrEndpoint.hpp:207, 224, 227) ──
    private bool _isAuthenticated;        // m_isAuthenticated
    private long _uniqueId;               // m_uniqueId (server-issued auth token, set after handshake)
    private bool _isMultiUserMode;        // m_isMultiUserMode (Phase 3)

    // ── HA / queue state (TcrEndpoint.hpp:189, 229-234) ──
    private bool _isQueueHosted;          // m_isQueueHosted
    private bool _isActiveEndpoint;       // m_isActiveEndpoint
    private int _serverQueueStatus;       // m_serverQueueStatus (enum ServerQueueStatus)
    private int _queueSize;               // m_queueSize
    private bool _isServerQueueStatusSet; // m_isServerQueueStatusSet
    private ushort _distributedMemId;     // m_distributedMemId

    // ── Counters (TcrEndpoint.hpp:187, 220-223) ──
    private int _numRegionListener;       // m_numRegionListener
    private int _numRegions;              // m_numRegions
    private int _notifyCount;             // m_notifyCount
    private uint _dupCount;               // m_dupCount

    // ── TCCM coordination semaphores (TcrEndpoint.hpp:208-210, 217) ──
    // cppcache passes binary_semaphore& from TCCM into the endpoint ctor;
    // .NET takes them as ctor refs (or via DI) when TCCM truly drives them.
    private SemaphoreSlim? _failoverSignal;            // failover_semaphore_
    private SemaphoreSlim? _cleanupSignal;             // cleanup_semaphore_
    private SemaphoreSlim? _redundancySignal;          // redundancy_semaphore_
    private readonly SemaphoreSlim _notificationCleanupSignal = new(0, int.MaxValue);  // notification_cleanup_semaphore_

    // ── Disposal flag ──
    private int _disposed;

#pragma warning restore CS0169, CS0414, CS0649

    public DnsEndPoint Endpoint => endpoint;

    /// <summary>Canonical <c>"host:port"</c> rendering for logs / registry keys.</summary>
    public string Name => $"{endpoint.Host}:{endpoint.Port}";

    public bool IsConnected => Volatile.Read(ref _connected) != 0;

    public int NumberOfTimesFailed => _numberOfTimesFailed;

    public bool IsAuthenticated => _isAuthenticated;

    public long UniqueId => Interlocked.Read(ref _uniqueId);

    public int NumRegions
    {
        get => Volatile.Read(ref _numRegions);
        set => Volatile.Write(ref _numRegions, value);
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
    public Task<int /*GfErrType*/> RegisterDMAsync(
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
            return Task.FromResult(/*GF_NOERR*/ 0);
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

        return Task.FromResult(/*GF_NOERR*/ 0);
    }

    /// <summary>
    /// Drop a DM. Mirrors cppcache <c>TcrEndpoint::unregisterDM</c>.
    /// When the last DM leaves and notification was started, close
    /// the subscription connection.
    /// </summary>
    public Task UnregisterDMAsync(
        bool clientNotification,
        object? distributionManager = null,
        CancellationToken ct = default)
    {
        // TODO: drop dm from _distMgrs; if last + clientNotification,
        //       stopNotifyReceiverAndCleanup.
        throw new NotImplementedException("TODO: TcrEndpoint.UnregisterDMAsync");
    }

    /// <summary>
    /// Send a request and wait for reply, choosing a connection from
    /// <c>_opConnections</c>. Mirrors cppcache
    /// <c>TcrEndpoint::send(request, reply)</c>.
    /// </summary>
    public Task<int /*GfErrType*/> SendAsync(
        object request,                   // TcrMessage
        object reply,                     // TcrMessageReply
        CancellationToken ct = default)
    {
        // TODO: dequeue from _opConnections; conn.Send(request, reply);
        //       enqueue back; on error → CloseFailedConnection +
        //       set _connected = 0 + signal _failoverSignal.
        throw new NotImplementedException("TODO: TcrEndpoint.SendAsync");
    }

    /// <summary>
    /// Send with retries against this endpoint's pool. Mirrors cppcache
    /// <c>TcrEndpoint::sendRequestWithRetry</c>.
    /// </summary>
    public Task<int /*GfErrType*/> SendRequestWithRetryAsync(
        object request,
        object reply,
        int maxSendRetries,
        CancellationToken ct = default)
    {
        throw new NotImplementedException("TODO: TcrEndpoint.SendRequestWithRetryAsync");
    }

    /// <summary>
    /// Open a fresh TCP/TLS connection and run the handshake. Mirrors
    /// cppcache <c>TcrEndpoint::createNewConnection</c>. Linux-only
    /// retry-under-lock variant <c>createNewConnectionWL</c> is bucket
    /// 1 (modern .NET sockets don't need it).
    /// </summary>
    public async Task<TcrConnection> CreateNewConnectionAsync(
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
            throw new NotImplementedException(
                "TODO Phase 2+: notification-channel handshake.");
        }
        _ = isSecondary;     // only meaningful with isClientNotification.
        _ = connectTimeout;  // TODO Phase 1.5: thread into TcrConnection.ConnectAsync
                             // once it grows a timeout parameter.

        ct.ThrowIfCancellationRequested();

        // cppcache LOGFINE entry log (TcrEndpoint.cpp:188-191) — simplified:
        // we don't have m_needToConnectInLock / appThreadRequest, so just
        // log host:port and let TcrConnection log its own handshake steps.
        logger.LogDebug(
            "TcrEndpoint.CreateNewConnection: opening request/response connection to {Host}:{Port}",
            endpoint.Host, endpoint.Port);

        // Pull TcrConnection through DI so its own deps (ILogger<TcrConnection>,
        // IOptions<GeodeClientOptions>, ClientProxyMembershipIdBuilder)
        // resolve cleanly. cppcache constructs TcrConnection directly with
        // the TcrConnectionManager reference; we let DI compose instead.
        var conn = ActivatorUtilities.CreateInstance<TcrConnection>(serviceProvider);

        try
        {
            // ConnectAsync bundles TCP connect (Nagle off) + the full
            // client/server handshake (steps 1-14). Mirrors cppcache
            // initTcrConnection: success or throw, no half-states.
            //   • GeodeException — server refused the handshake (REPLY_OK
            //     not received) or pointed at a locator port.
            //   • SocketException / IOException — TCP failure.
            //   • OperationCanceledException — ct cancelled.
            await conn.ConnectAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Send <c>MessageType.Ping</c> to update <see cref="IsConnected"/>.
    /// Mirrors cppcache <c>TcrEndpoint::pingServer</c>.
    /// </summary>
    public Task PingAsync(object? poolDM = null, CancellationToken ct = default)
    {
        // TODO: pick a conn (or create one); send Ping; on success
        //       _connected = 1, _pingTimeouts = 0; on failure,
        //       _pingTimeouts++ and possibly setConnected(false).
        throw new NotImplementedException("TODO: TcrEndpoint.PingAsync");
    }

    /// <summary>
    /// Receiver loop body for the subscription channel; mirrors
    /// cppcache <c>TcrEndpoint::receiveNotification</c>. Drives event
    /// dispatch to registered listeners.
    /// </summary>
    public Task ReceiveNotificationsAsync(CancellationToken ct = default)
    {
        // TODO Phase 2+: blocking read on _notifyConnection, decode
        //   message, dispatch to ThinClientRegion listeners. Phase 2+.
        throw new NotImplementedException("TODO: TcrEndpoint.ReceiveNotificationsAsync");
    }

    /// <summary>
    /// Run the auth handshake on a freshly-opened connection. Mirrors
    /// cppcache <c>TcrEndpoint::authenticateEndpoint</c>.
    /// </summary>
    public Task AuthenticateEndpointAsync(object connection, CancellationToken ct = default)
    {
        // TODO Phase 3 (security): send credentials, read uniqueId.
        throw new NotImplementedException("TODO: TcrEndpoint.AuthenticateEndpointAsync");
    }

    /// <summary>
    /// Flip <see cref="IsConnected"/>. Mirrors cppcache
    /// <c>TcrEndpoint::setConnected</c> /
    /// <c>setConnectionStatus</c>.
    /// </summary>
    public void SetConnected(bool connected)
    {
        Interlocked.Exchange(ref _connected, connected ? 1 : 0);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;

        _connectLock.Dispose();
        _notificationCleanupSignal.Dispose();

        // TODO: close _opConnections, _notifyConnection, await
        //       _notifyReceiver task; release endpoint resources.
        return ValueTask.CompletedTask;
    }
}
