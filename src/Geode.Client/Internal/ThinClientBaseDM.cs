using System.Threading.Channels;

namespace Geode.Client.Internal;

/// <summary>
/// Abstract base of the distribution-manager hierarchy. Mirrors
/// cppcache <c>ThinClientBaseDM</c>
/// (<c>cppcache/src/ThinClientBaseDM.hpp/.cpp</c>) — the common
/// contract every DM (simple <see cref="ThinClientDistributionManager"/>
/// or pool <see cref="ThinClientPoolDM"/>) must satisfy.
/// </summary>
/// <remarks>
/// <para>
/// Owns: lifecycle flags, async chunk-context queue, security /
/// multi-user hooks (default empty), interest-registration template.
/// Pure abstract: <see cref="SendSyncRequestAsync"/> and
/// <see cref="SendRequestToEndpointAsync"/> — request dispatch is
/// each DM's own job.
/// </para>
/// <para>
/// Phase 1.5 we only build the pool variant; the base + simple DM
/// shells exist so their inheritance / registration paths line up
/// 1:1 with cppcache during implementation.
/// </para>
/// </remarks>
internal abstract class ThinClientBaseDM : IAsyncDisposable
{
    protected readonly TcrConnectionManager ConnManager;     // m_connManager
    protected readonly object? Region;                        // m_region (ThinClientRegion*)
    protected bool InitDone;                                  // m_initDone
    protected bool ClientNotification;                        // m_clientNotification

    /// <summary>
    /// Async chunked-response queue. Mirrors cppcache
    /// <c>m_chunks</c> + <c>m_chunkProcessor</c> Task.
    /// </summary>
    protected readonly Channel<object> Chunks =
        Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    protected Task? ChunkProcessor;
    protected readonly CancellationTokenSource ChunkCts = new();

    private int _disposed;

    protected ThinClientBaseDM(TcrConnectionManager connManager, object? region)
    {
        ArgumentNullException.ThrowIfNull(connManager);
        ConnManager = connManager;
        Region = region;
    }

    // ── Lifecycle ──────────────────────────────────────────────

    /// <summary>
    /// One-time init. Mirrors cppcache <c>ThinClientBaseDM::init()</c>:
    /// optionally start the chunk-processor task, set
    /// <see cref="InitDone"/>. Derived classes call <c>base.InitAsync</c>
    /// at the end of their own init.
    /// </summary>
    public virtual Task InitAsync(CancellationToken ct = default)
    {
        // TODO: if options.EnableChunkHandlerThread → StartChunkProcessor.
        InitDone = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirrors cppcache <c>destroy(keepalive)</c>: stop chunk
    /// processor, mark not-initialised.
    /// </summary>
    public virtual Task DestroyAsync(bool keepAlive = false, CancellationToken ct = default)
    {
        if (!InitDone) return Task.CompletedTask;
        // TODO: stopChunkProcessor; await ChunkProcessor.
        InitDone = false;
        return Task.CompletedTask;
    }

    // ── Pure abstract: each DM implements its own dispatch ─────

    /// <summary>
    /// Send a request and block until reply / error. Mirrors cppcache
    /// pure-virtual <c>sendSyncRequest</c>.
    /// </summary>
    public abstract Task<int /*GfErrType*/> SendSyncRequestAsync(
        object request,                  // TcrMessage
        object reply,                    // TcrMessageReply
        bool attemptFailover = true,
        bool isBackgroundThread = false,
        CancellationToken ct = default);

    /// <summary>
    /// Send to a specific endpoint, no DM-level routing. Mirrors
    /// cppcache pure-virtual <c>sendRequestToEP</c>.
    /// </summary>
    public abstract Task<int /*GfErrType*/> SendRequestToEndpointAsync(
        object request,
        object reply,
        TcrEndpoint endpoint,
        CancellationToken ct = default);

    // ── Template methods (concrete; delegate to derived) ───────

    /// <summary>
    /// Interest registration helper. Mirrors cppcache
    /// <c>sendSyncRequestRegisterInterest</c> — when
    /// <paramref name="endpoint"/> is null delegate to
    /// <see cref="SendSyncRequestAsync"/>; otherwise delegate to
    /// <see cref="SendRequestToEndpointAsync"/>.
    /// </summary>
    public virtual Task<int /*GfErrType*/> SendSyncRequestRegisterInterestAsync(
        object request,
        object reply,
        bool attemptFailover = true,
        TcrEndpoint? endpoint = null,
        CancellationToken ct = default)
    {
        if (endpoint is null)
        {
            return SendSyncRequestAsync(request, reply, attemptFailover, false, ct);
        }
        return endpoint.IsConnected
            ? SendRequestToEndpointAsync(request, reply, endpoint, ct)
            : Task.FromResult(/*GF_NOTCON*/ -1);
    }

    // ── Empty virtual hooks (override in derived if needed) ────

    public virtual Task FailoverAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual void AcquireFailoverLock() { }
    public virtual void ReleaseFailoverLock() { }
    public virtual void AcquireRedundancyLock() { }
    public virtual void ReleaseRedundancyLock() { }
    public virtual void TriggerRedundancyThread() { }

    public virtual bool IsSecurityOn => false;        // TODO: ConnManager.HasAuthInitialize when wired
    public virtual bool IsMultiUserMode => false;

    public virtual void BeforeSendingRequest(object request, object connection) { }
    public virtual void AfterSendingRequest(object request, object reply, object connection) { }

    public virtual TcrEndpoint? ActiveEndpoint => null;
    public virtual int NumberOfEndpoints => 0;

    public virtual bool IsEndpointAttached(TcrEndpoint endpoint) => false;
    public virtual void IncConnectedEndpoints() { }
    public virtual void DecConnectedEndpoints() { }

    public virtual Task<int /*GfErrType*/> RegisterInterestForRegionAsync(
        TcrEndpoint endpoint,
        object? region = null,
        CancellationToken ct = default)
        => Task.FromResult(/*GF_NOERR*/ 0);

    /// <summary>
    /// Push a chunked-response context onto <see cref="Chunks"/> for
    /// the chunk-processor to consume. Mirrors cppcache
    /// <c>queueChunk</c>.
    /// </summary>
    public void QueueChunk(object chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        Chunks.Writer.TryWrite(chunk);
    }

    // ── Static error classifiers (cppcache inline static) ──────

    /// <summary>Mirrors cppcache <c>isFatalError(GfErrType)</c>.</summary>
    public static bool IsFatalError(int err)
    {
        // TODO: port the cppcache GfErrType enum table once GfErrType lands.
        return false;
    }

    /// <summary>Mirrors cppcache <c>isFatalClientError(GfErrType)</c>.</summary>
    public static bool IsFatalClientError(int err)
    {
        // TODO: same as IsFatalError.
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await DestroyAsync(keepAlive: false).ConfigureAwait(false);
        ChunkCts.Cancel();
        ChunkCts.Dispose();
    }
}
