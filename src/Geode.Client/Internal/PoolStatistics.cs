using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Geode.Client.Internal;

internal class PoolStatistics(string poolName)
{
    // ── cppcache PoolStats 27-field catalogue (PoolStatistics.cpp:34-122) ──
    //
    // Gauge(瞬時值)— 6 個
    //   locators / servers / subscriptionServers
    //   poolConnections(= m_poolSize)
    //   connectionWaitsInProgress、clientOpsInProgress
    //
    // Counter(累積值)— 15 個
    //   locatorRequests / locatorResponses
    //   connects / disconnects(總計)
    //   minPoolSizeConnects、loadConditioningConnects
    //   idleDisconnects、loadConditioningDisconnects
    //   connectionWaits(完成的 wait 次數)
    //   clientOps(成功) / clientOpFailures / clientOpTimeouts
    //   queryExecutions
    //   processedDeltaMessages、deltaMessageFailures
    //
    // Time / bytes counter(累積 ns 或 bytes)— 6 個
    //   connectionWaitTime / clientOpTime / queryExecutionTime
    //   processedDeltaMessagesTime
    //   receivedBytes、messagesBeingReceived

    private static readonly string AssemblyVersion =
        typeof(PoolStatistics).Assembly.GetName().Version?.ToString() ?? "0.0.0";


    // Meter
    readonly static Meter _meter = new ("Geode.Client.Pool", AssemblyVersion);

    // Background locator-list refresh loop (UpdateLocatorsLocalAsync,
    // wire: LocatorListRequest -54 / LocatorListResponse -51).
    readonly static Histogram<double> _locatorListRequestTime = _meter.CreateHistogram<double>(
        "LocatorListRequestTime",
        unit: "s",
        description: "Elapsed time of LocatorListRequest RPCs issued by the pool's background locator-list refresh loop.");

    // On-demand endpoint selection (SelectEndpointFromLocatorAsync,
    // wire: ClientConnectionRequest -53 / ClientConnectionResponse -50).
    // cppcache `incLoctorRequests` / `incLoctorResposes` (PoolStatistics.cpp:43-50)
    // counted the request + response halves of this RPC; merged here into one
    // Histogram (.Count subsumes both — outcome split deferred until needed).
    readonly static Histogram<double> _clientConnectionRequestTime = _meter.CreateHistogram<double>(
        "ClientConnectionRequestTime",
        unit: "s",
        description: "Elapsed time of ClientConnectionRequest RPCs issued by the pool when opening a new server connection through a locator.");

    public void LocatorListRequest(TimeSpan elapsed)
    {
        _locatorListRequestTime.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("poolName", poolName));
    }

    public void ClientConnectionRequest(TimeSpan elapsed)
    {
        _clientConnectionRequestTime.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("poolName", poolName));
    }


    // Lifetime totals — every successful conn open / close ticks these,
    // regardless of cause. cppcache connects / disconnects
    // (PoolStatistics.cpp:53-58, IntCounter pair). Combined with the
    // PoolConnections gauge: connects - disconnects ≈ PoolConnections
    // at steady state; rates give churn / shrink velocity.
    readonly static Counter<int> _poolConnects = _meter.CreateCounter<int>(
        "PoolConnects",
        unit: "connections",
        description: "Total connections opened by the pool over its lifetime, all causes combined.");

    readonly static Counter<int> _poolDisconnects = _meter.CreateCounter<int>(
        "PoolDisconnects",
        unit: "connections",
        description: "Total connections closed by the pool over its lifetime, all causes combined.");

    public void PoolConnect() =>
        _poolConnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    public void PoolDisconnect() =>
        _poolDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));


    // Load conditioning — periodic forced rotation of long-lived conns
    // (CleanStaleConnectionsAsync replace path).
    // cppcache loadConditioningConnects / loadConditioningDisconnects
    // (PoolStatistics.cpp:63-73, IntCounter pair).
    readonly static Counter<int> _loadConditioningConnects = _meter.CreateCounter<int>(
        "LoadConditioningConnects",
        unit: "connections",
        description: "Total connections opened to replace load-conditioning-expired conns.");

    readonly static Counter<int> _loadConditioningDisconnects = _meter.CreateCounter<int>(
        "LoadConditioningDisconnects",
        unit: "connections",
        description: "Total connections closed because they hit the load-conditioning expiry threshold.");

    public void LoadConditioningConnect() =>
        _loadConditioningConnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    public void LoadConditioningDisconnect() =>
        _loadConditioningDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    // Idle shrink — conn unused beyond IdleTimeout while _poolSize > Min,
    // closed without replacement (CleanStaleConnectionsAsync pure-shrink path).
    // cppcache idleDisconnects (PoolStatistics.cpp:66-69, IntCounter).
    readonly static Counter<int> _idleDisconnects = _meter.CreateCounter<int>(
        "IdleDisconnects",
        unit: "connections",
        description: "Total connections closed because they sat idle beyond the IdleTimeout while the pool was above MinConnections.");

    public void IdleDisconnect() =>
        _idleDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));


    // Ping loop observability — no cppcache equivalent (cppcache PoolStats
    // has no ping counters); our own design for "is the ping loop alive"
    // + "are endpoints surviving health probes".
    readonly static Counter<int> _pingTicks = _meter.CreateCounter<int>(
        "PingTicks",
        unit: "sweeps",
        description: "Count of ping-loop sweeps completed by the pool's background ping task.");

    readonly static Counter<int> _pingSuccesses = _meter.CreateCounter<int>(
        "PingSuccesses",
        unit: "pings",
        description: "Count of endpoint pings that returned without throwing and left the endpoint still connected.");

    public void PingTick() =>
        _pingTicks.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    public void PingSuccess() =>
        _pingSuccesses.Add(1, new KeyValuePair<string, object?>("poolName", poolName));


    // Gauges — pull-based ObservableGauge with a static reader registry
    // keyed by poolName. cppcache uses push (`setCurPoolConnections` etc.)
    // on each modify; .NET idiomatic pull lets the listener decide cadence
    // and avoids missing a modify site.

    // poolConnections (cppcache PoolStatistics.cpp:51-52, IntGauge m_poolSize).
    private static readonly ConcurrentDictionary<string, Func<int>> _poolConnectionsReaders = new();

    readonly static ObservableGauge<int> _poolConnections = _meter.CreateObservableGauge(
        "PoolConnections",
        observeValues: ObservePoolConnections,
        unit: "connections",
        description: "Current number of connections held by the pool. Mirrors cppcache `poolConnections` IntGauge (m_poolSize).");

    private static IEnumerable<Measurement<int>> ObservePoolConnections()
    {
        foreach (var (name, reader) in _poolConnectionsReaders)
        {
            yield return new Measurement<int>(
                reader(),
                new KeyValuePair<string, object?>("poolName", name));
        }
    }

    public void SetPoolConnectionsReader(Func<int> reader) =>
        _poolConnectionsReaders[poolName] = reader;

    public void ClearPoolConnectionsReader() =>
        _poolConnectionsReaders.TryRemove(poolName, out _);


    // Activity
    readonly static ActivitySource _activitySource = new ("Geode.Client.Pool", AssemblyVersion);

    public Activity? StartLocatorListRequest() =>
        _activitySource.StartActivity("LocatorListRequest", ActivityKind.Client)?.SetTag("poolName", poolName);

    public Activity? StartClientConnectionRequest() =>
        _activitySource.StartActivity("ClientConnectionRequest", ActivityKind.Client)?.SetTag("poolName", poolName);




}
