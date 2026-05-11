using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Geode.Client.Options;
using Geode.Client.Protocol;
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
#pragma warning disable CS0169, CS0414, CS0649, CS9113 // placeholder fields mirroring ThinClientPoolDM; wired up phase by phase
internal sealed class ThinClientPoolDM(
    CacheXmlPoolOptions xmlPool,
    GeodeClientOptions options,
    TcrConnectionManager connManager,
    ILogger<ThinClientPoolDM> logger) : ThinClientBaseDM(connManager, region: null), IPool
{



    // ── Endpoint registry (ThinClientPoolDM.hpp m_endpoints) ──
    // Pool's view onto TCCM-owned TcrEndpoint instances. Same object
    // identity as TcrConnectionManager._endpoints; this map tracks
    // which endpoints THIS pool currently holds a ref on so destroy
    // knows what to release. Key uses DnsEndPoint default equality.
    private readonly ConcurrentDictionary<DnsEndPoint, TcrEndpoint> _endpoints =
        new();                                    // m_endpoints

    // ── Idle connection queue (cppcache inherits ConnectionQueue<TcrConnection>) ──
    // Unbounded for Phase 1.1; Phase 1.5 may bound by MaxConnections.
    // Channel auto-wakes a pending reader on WriteAsync — replaces
    // cppcache's conn_semaphore_.release().
    private readonly Channel<TcrConnection> _opConnections =
        Channel.CreateUnbounded<TcrConnection>();    // m_opConnections
    private int _poolSize;                            // m_poolSize (Interlocked)

    // ── Static-server round-robin cursor (ThinClientPoolDM.cpp:608) ──
    // Guarded by _endpointSelectionLock; mirrors cppcache m_server +
    // m_endpointSelectionLock. SelectEndpointAsync reads + post-increments
    // (with wrap) under the lock.
    private int _server;                                              // m_server
    private readonly Lock _endpointSelectionLock = new();             // m_endpointSelectionLock

    // ── Locator (Phase 1.5) ──
    private object? _locatorHelper;                // m_locHelper (ThinClientLocatorHelper)

    // ── Background workers (Phase 1.5, pool-mode equivalents of TCCM trio) ──
    private Task? _pingLoop;                       // m_pingTask
    private Task? _connManageLoop;                 // m_connManageTask
    private Task? _updateLocatorLoop;              // m_updateLocatorListTask
    private readonly SemaphoreSlim _pingSignal = new(0, int.MaxValue);
    private readonly SemaphoreSlim _connManageSignal = new(0, int.MaxValue);
    private readonly SemaphoreSlim _updateLocatorSignal = new(0, int.MaxValue);
    private PeriodicTimer? _pingTimer;
    private readonly CancellationTokenSource _backgroundCts = new();

    // ── Single-hop metadata (Phase 4) ──
    private object? _clientMetadataService;        // m_clientMetadataService

    // ── HA subscription (Phase 2+) — inherited from base TCCM via composition ──
    private object? _redundancyManager;            // m_redundancyManager

    // ── Sticky transactions (Phase 6) ──
    private object? _stickyManager;                // ThinClientStickyManager
    private bool _isSticky;                        // m_sticky flag

    // ── State flags (ThinClientPoolDM.hpp:203-204) ──
    private int _isDestroyed;                      // m_isDestroyed (Interlocked 0/1)
    private int _destroyPending;                   // m_destroyPending (Interlocked 0/1)

    // ── Stats (Phase 1.5 thin wrapper around Meter) ──
    private object? _stats;                        // m_stats (PoolStats)

#pragma warning restore CS0169, CS0414, CS0649

    /// <summary>
    /// 0 = <see cref="InitAsync"/> not run, 1 = ran. Mirrors cppcache
    /// pool DM's one-shot init guard; gated by
    /// <see cref="Interlocked.Exchange(ref int, int)"/>.
    /// </summary>
    private int _initGuard;

    public string Name => xmlPool.Name;
    public bool IsDestroyed => Volatile.Read(ref _isDestroyed) != 0;

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
        _ = keepAlive;   // TODO Phase 2+: route into per-connection CloseConnection.

        // 1. Idempotent destroy guard.
        if (Interlocked.Exchange(ref _isDestroyed, 1) != 0)
        {
            return;
        }

        // 2. Signal every background loop to stop.
        _backgroundCts.Cancel();

        // 3. Await each loop's graceful exit. OperationCanceledException
        //    is expected here — that IS the graceful exit signal.
        if (_connManageLoop is not null)
        {
            try { await _connManageLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        // TODO Phase 1.5: same await pattern for _pingLoop /
        //   _updateLocatorLoop once they're launched.

        // 4. Dispose timers + sync primitives owned by this pool.
        _pingTimer?.Dispose();
        _pingSignal.Dispose();
        _connManageSignal.Dispose();
        _updateLocatorSignal.Dispose();
        _backgroundCts.Dispose();

        // 5. TODO Phase 1.1: drain _opConnections — for each TcrConnection:
        //       await conn.SendAsync(MessageType.CloseConnection bytes);
        //       await conn.DisposeAsync();
        //    TODO Phase 1.2+: dispose endpoints in _endpoints (unregister
        //       from TCCM, close subscription channel if any).
        //    TODO Phase 1.5: drain TCCM's release queues.
    }

    // ── Lifecycle (override base + add pool-mode init) ──────────

    public override Task InitAsync(CancellationToken ct = default)
    {
        // ── 1. Pre-check ────────────────────────────────────────
        ct.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _isDestroyed) != 0)
        {
            throw new ObjectDisposedException(nameof(ThinClientPoolDM));
        }

        // Idempotent: first caller wins. Mirrors cppcache m_initGuard
        // semantics — set BEFORE doing work, no rollback on failure.
        // Concurrent re-entry is prevented by Cache.EnsureInitializedAsync's
        // SemaphoreSlim, so this is purely a "skip if already ran" check.
        if (Interlocked.Exchange(ref _initGuard, 1) != 0)
        {
            return Task.CompletedTask;
        }

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

        // TODO Phase 1.5: launch the rest of the workers and timers:
        //   • _pingLoop = Task.Run(() => PingLoopAsync(_backgroundCts.Token));
        //       drives endpoint pings on _xmlPool.PingInterval
        //                                  ?? _options.Pool.PingInterval.
        //   • _updateLocatorLoop = Task.Run(() => UpdateLocatorLoopAsync(_backgroundCts.Token));
        //       only when _xmlPool.Locators.Count > 0.
        //   • _pingTimer = new PeriodicTimer(pingInterval);
        //   • RemoteQueryService.InitAsync   — Phase 1.4 (pool-scoped QS).
        //   • Statistics sampler             — bucket-1 (Meter-based).
    }

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
        var interval = xmlPool.IdleTimeout;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);

                try
                {
                    // TODO Phase 1.5: await CleanStaleConnectionsAsync(ct);
                    await RestoreMinConnectionsAsync(ct).ConfigureAwait(false);
                    // TODO Phase 6:    await CleanStickyConnectionsAsync(ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Survive transient errors so a single bad tick
                    // doesn't kill the loop. Phase 1.5: log via
                    // ILogger.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // graceful shutdown via _backgroundCts.Cancel().
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
        while (Volatile.Read(ref _poolSize) < min)
        {
            ct.ThrowIfCancellationRequested();
            var conn = await CreatePoolConnectionAsync(ct).ConfigureAwait(false);
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
    private Task<DnsEndPoint> SelectEndpointAsync(CancellationToken ct = default)
    {
        // Locator branch (priority) — cppcache ThinClientPoolDM.cpp:579-602.
        if (xmlPool.Locators.Count > 0)
        {
            // TODO Phase 1.5: await _locatorHelper.GetEndpointForNewFwdConnAsync(
            //   excludeServers, _xmlPool.ServerGroup, currentServer, ct);
            // then return new DnsEndPoint(outEndpoint.Host, outEndpoint.Port).
            throw new NotImplementedException(
                "TODO Phase 1.5: locator branch (ThinClientLocatorHelper).");
        }

        // Static server branch — cppcache ThinClientPoolDM.cpp:603-628.
        if (xmlPool.Servers.Count > 0)
        {
            // Round-robin: read cursor, post-increment with wrap, all under
            // the selection lock. Phase 1.5 will turn this into a do-while
            // that skips entries in `excludeServers` (cppcache excludeServer
            // helper) and throws NotConnectedException once every server is
            // excluded.
            int position;
            CacheXmlHostPort server;
            lock (_endpointSelectionLock)
            {
                if (_server >= xmlPool.Servers.Count)
                {
                    _server = 0;
                }
                position = _server;
                server = xmlPool.Servers[position];
                _server++;
            }

            // Convert from the Options-layer CacheXmlHostPort (XML/JSON
            // bindable, mutable) to the runtime-layer DnsEndPoint (BCL,
            // immutable, hashable). This is the single conversion point.
            var endpoint = new DnsEndPoint(server.Host, server.Port);

            // cppcache: LOGFINE("ThinClientPoolDM: Selecting endpoint [%s] from position %d", ...)
            logger.LogDebug(
                "ThinClientPoolDM: Selecting endpoint [{Host}:{Port}] from position {Position}",
                endpoint.Host, endpoint.Port, position);

            return Task.FromResult(endpoint);
        }

        // Unreachable: AddGeodeClient options validation rejects pools with
        // neither Locators nor Servers. Mirrors cppcache's
        // IllegalStateException("No locators or servers provided").
        throw new InvalidOperationException(
            $"Pool '{xmlPool.Name}' has neither Locators nor Servers configured.");
    }

    /// <summary>
    /// Open exactly one new <see cref="TcrConnection"/>. Mirrors
    /// cppcache <c>ThinClientPoolDM::createPoolConnection()</c>:
    /// select an endpoint (locator or static server list), get-or-
    /// create its <see cref="TcrEndpoint"/> from the registry,
    /// open the connection on it, enqueue. Returns <c>null</c> when
    /// no endpoint can currently be reached.
    /// </summary>
    private async Task<TcrConnection?> CreatePoolConnectionAsync(CancellationToken ct)
    {
        // Step 1: pick the endpoint to connect to (locator or static
        // server list). cppcache: selectEndpoint(excludeServers, currentServer).
        var location = await SelectEndpointAsync(ct).ConfigureAwait(false);

        // Step 2: get-or-create the pool's reference to that endpoint.
        // cppcache: LOGFINE("Connecting to %s", ...) + addEP(epNameStr).
        logger.LogDebug("Connecting to {Host}:{Port}", location.Host, location.Port);
        var endpoint = await AddEPAsync(location, ct).ConfigureAwait(false);

        // Step 3: open the TCP socket + run the handshake on this
        // endpoint. cppcache passes connectTimeout from SystemProperties;
        // we currently let TcrEndpoint apply its own default (Phase 1.5
        // will plumb xmlPool.ConnectTimeout / options.Pool.ConnectTimeout
        // through here once those options surface again on this DM).
        // Phase 1.1: a single endpoint, so on failure we let the
        // exception bubble — Phase 1.5 will wrap this in the retry loop
        // with excludeServers + isFatalError classification.
        var conn = await endpoint
            .CreateNewConnectionAsync(
                isClientNotification: false,
                isSecondary: false,
                connectTimeout: null,
                ct: ct)
            .ConfigureAwait(false);

        // Step 4: mark the endpoint healthy and grow the pool counter.
        // cppcache (ThinClientPoolDM.cpp:1796-1801):
        //   ep->setConnected();
        //   if (++m_poolSize > min) getStats().incLoadCondConnects();
        //   getStats().incPoolConnects();
        //   getStats().setCurPoolConnections(m_poolSize);
        // The conn_semaphore_.release() at the end of cppcache's function
        // is unnecessary here — Channel<TcrConnection>.Writer.WriteAsync
        // (driven by RestoreMinConnectionsAsync after we return) wakes
        // any pending reader automatically.
        endpoint.SetConnected(true);
        Interlocked.Increment(ref _poolSize);
        // TODO Phase 1.5: stats — incPoolConnects, setCurPoolConnections,
        //   and incLoadCondConnects when _poolSize > min.

        // Step 5: return the fresh conn. cppcache returns it via out
        // param; the caller (restoreMinConnections during warm-up,
        // sendSyncRequest during queue starvation) decides whether to
        // enqueue or use immediately.
        return conn;
    }

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

    // ── ThinClientBaseDM pure abstract ──────────────────────────

    public override Task<int /*GfErrType*/> SendSyncRequestAsync(
        object request,
        object reply,
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default)
    {
        // TODO Phase 1.2: dequeue conn → endpoint.SendAsync → enqueue.
        //   On error: failover loop (Phase 1.5).
        throw new NotImplementedException("TODO: ThinClientPoolDM.SendSyncRequestAsync");
    }

    public override Task<int /*GfErrType*/> SendRequestToEndpointAsync(
        object request,
        object reply,
        TcrEndpoint endpoint,
        CancellationToken ct = default)
    {
        // TODO Phase 1.2 / 2+: targeted send for register-interest /
        //   subscription. Bypass the queue's load-balancing.
        throw new NotImplementedException("TODO: ThinClientPoolDM.SendRequestToEndpointAsync");
    }

    // ── Connection lifecycle helpers (Phase 1.5) ────────────────

    // TODO Phase 1.5:
    //   Task<TcrConnection> GetConnectionFromQueueAsync(CancellationToken ct);
    //   ValueTask PutInQueueAsync(TcrConnection conn);
    //   Task PingServerAsync(CancellationToken ct);
    //   Task RestoreMinConnectionsAsync(CancellationToken ct);
    //   Task CleanStaleConnectionsAsync(CancellationToken ct);
}
