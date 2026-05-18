using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Geode.Client.Internal;

/// <summary>
/// Pool-scoped Meter + ActivitySource sink. Mirrors cppcache
/// <c>PoolStats</c> (<c>PoolStatistics.{cpp,hpp}</c>) — emits per-pool
/// counters / histograms / gauges via
/// <see cref="System.Diagnostics.Metrics"/> so OTel / Prometheus exporters
/// scrape without touching every PoolDM modify site.
/// </summary>
/// <remarks>
/// cppcache <c>PoolStats</c> 27-field catalogue
/// (<c>PoolStatistics.cpp:34-122</c>): gauge × 6, counter × 15,
/// time/bytes × 6. Ported so far: <c>PoolConnects</c> / <c>PoolDisconnects</c>
/// / <c>MinPoolSizeConnects</c> / <c>LoadConditioningConnects</c> /
/// <c>LoadConditioningDisconnects</c> / <c>IdleDisconnects</c> /
/// <c>PoolConnections</c> / <c>Locators</c> / <c>Servers</c> gauges /
/// <c>LocatorListRequestTime</c> / <c>ClientConnectionRequestTime</c>
/// (the last two merge cppcache's request+response halves into one
/// Histogram each). Non-cppcache additions: <c>ConnectedServers</c>
/// gauge (surfaces cppcache's internal <c>connected_endpoints_</c> atomic);
/// <c>PingSweepTime</c> / <c>EndpointPingTime</c> (our own ping-loop
/// liveness signals — cppcache PoolStats has no ping counters).
/// </remarks>
internal class PoolStatistics(string poolName)
{
    private static readonly string AssemblyVersion =
        typeof(PoolStatistics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Shared Meter for all pool-scoped instruments.</summary>
    readonly static Meter _meter = new("Geode.Client.Pool", AssemblyVersion);

    /// <summary>
    /// Elapsed time of <c>LocatorListRequest</c> RPCs issued by the
    /// background locator-list refresh loop
    /// (<see cref="ThinClientPoolDM"/>'s <c>UpdateLocatorsLocalAsync</c>;
    /// wire DSFid -54 / -51).
    /// </summary>
    readonly static Histogram<double> _locatorListRequestTime = _meter.CreateHistogram<double>(
        "LocatorListRequestTime",
        unit: "s",
        description: "Elapsed time of LocatorListRequest RPCs issued by the pool's background locator-list refresh loop.");

    /// <summary>
    /// Elapsed time of <c>ClientConnectionRequest</c> RPCs (on-demand
    /// endpoint selection via locator; wire DSFid -53 / -50). cppcache
    /// <c>incLoctorRequests</c> / <c>incLoctorResposes</c>
    /// (<c>PoolStatistics.cpp:43-50</c>) counted request + response halves
    /// separately; merged here into one Histogram (<c>.Count</c> subsumes
    /// both).
    /// </summary>
    readonly static Histogram<double> _clientConnectionRequestTime = _meter.CreateHistogram<double>(
        "ClientConnectionRequestTime",
        unit: "s",
        description: "Elapsed time of ClientConnectionRequest RPCs issued by the pool when opening a new server connection through a locator.");

    /// <summary>Record one <see cref="_locatorListRequestTime"/> sample.</summary>
    public void LocatorListRequest(TimeSpan elapsed) =>
        _locatorListRequestTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>Record one <see cref="_clientConnectionRequestTime"/> sample.</summary>
    public void ClientConnectionRequest(TimeSpan elapsed) =>
        _clientConnectionRequestTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Total connections opened by the pool, all causes combined. Mirrors
    /// cppcache <c>connects</c> (<c>PoolStatistics.cpp:53-58</c>).
    /// Combined with <see cref="_poolConnections"/>: <c>connects - disconnects ≈ PoolConnections</c>
    /// at steady state; rates give churn / shrink velocity.
    /// </summary>
    readonly static Counter<int> _poolConnects = _meter.CreateCounter<int>(
        "PoolConnects",
        unit: "connections",
        description: "Total connections opened by the pool over its lifetime, all causes combined.");

    /// <summary>
    /// Total connections closed by the pool, all causes combined. Pair
    /// with <see cref="_poolConnects"/>. Mirrors cppcache <c>disconnects</c>
    /// (<c>PoolStatistics.cpp:53-58</c>).
    /// </summary>
    readonly static Counter<int> _poolDisconnects = _meter.CreateCounter<int>(
        "PoolDisconnects",
        unit: "connections",
        description: "Total connections closed by the pool over its lifetime, all causes combined.");

    /// <summary>Bump <see cref="_poolConnects"/>.</summary>
    public void PoolConnect() =>
        _poolConnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>Bump <see cref="_poolDisconnects"/>.</summary>
    public void PoolDisconnect() =>
        _poolDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Load conditioning — periodic forced rotation of long-lived conns
    /// (<c>CleanStaleConnectionsAsync</c> replace path). Mirrors cppcache
    /// <c>loadConditioningConnects</c> (<c>PoolStatistics.cpp:63-73</c>).
    /// </summary>
    readonly static Counter<int> _loadConditioningConnects = _meter.CreateCounter<int>(
        "LoadConditioningConnects",
        unit: "connections",
        description: "Total connections opened to replace load-conditioning-expired conns.");

    /// <summary>
    /// Pair with <see cref="_loadConditioningConnects"/>. Mirrors cppcache
    /// <c>loadConditioningDisconnects</c> (<c>PoolStatistics.cpp:63-73</c>).
    /// </summary>
    readonly static Counter<int> _loadConditioningDisconnects = _meter.CreateCounter<int>(
        "LoadConditioningDisconnects",
        unit: "connections",
        description: "Total connections closed because they hit the load-conditioning expiry threshold.");

    /// <summary>Bump <see cref="_loadConditioningConnects"/>.</summary>
    public void LoadConditioningConnect() =>
        _loadConditioningConnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>Bump <see cref="_loadConditioningDisconnects"/>.</summary>
    public void LoadConditioningDisconnect() =>
        _loadConditioningDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Min-pool-size restore — conn opened by the conn-management loop
    /// (<c>RestoreMinConnectionsAsync</c>) to bring <c>_poolSize</c> back
    /// up to <see cref="Options.CachePoolOptions.MinConnections"/>. Mirrors
    /// cppcache <c>minPoolSizeConnects</c> (<c>PoolStatistics.cpp:59-62</c>).
    /// </summary>
    readonly static Counter<int> _minPoolSizeConnects = _meter.CreateCounter<int>(
        "MinPoolSizeConnects",
        unit: "connections",
        description: "Total connections opened by the conn-management loop to maintain MinConnections.");

    /// <summary>Bump <see cref="_minPoolSizeConnects"/>.</summary>
    public void MinPoolSizeConnect() =>
        _minPoolSizeConnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Idle shrink — conn unused beyond
    /// <see cref="Options.CachePoolOptions.IdleTimeout"/> while
    /// <c>_poolSize &gt; MinConnections</c>, closed without replacement
    /// (<c>CleanStaleConnectionsAsync</c> pure-shrink path). Mirrors
    /// cppcache <c>idleDisconnects</c> (<c>PoolStatistics.cpp:66-69</c>).
    /// </summary>
    readonly static Counter<int> _idleDisconnects = _meter.CreateCounter<int>(
        "IdleDisconnects",
        unit: "connections",
        description: "Total connections closed because they sat idle beyond the IdleTimeout while the pool was above MinConnections.");

    /// <summary>Bump <see cref="_idleDisconnects"/>.</summary>
    public void IdleDisconnect() =>
        _idleDisconnects.Add(1, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Elapsed time of one <c>PingServerLocalAsync</c> sweep. No cppcache
    /// equivalent — our own design for "is the ping loop alive".
    /// <c>.Count</c> subsumes the old <c>PingTicks</c> counter; recorded in
    /// <c>finally</c> so exception paths still tick.
    /// </summary>
    readonly static Histogram<double> _pingSweepTime = _meter.CreateHistogram<double>(
        "PingSweepTime",
        unit: "s",
        description: "Elapsed time of one ping-loop sweep over the pool's connected endpoints.");

    /// <summary>
    /// Elapsed time of one <c>endpoint.PingAsync</c> that returned without
    /// throwing AND left the endpoint still connected. <c>.Count</c>
    /// subsumes the old <c>PingSuccesses</c> counter. Pair with
    /// <see cref="_pingSweepTime"/>.
    /// </summary>
    readonly static Histogram<double> _endpointPingTime = _meter.CreateHistogram<double>(
        "EndpointPingTime",
        unit: "s",
        description: "Elapsed time of one successful endpoint ping (returned without throwing and left the endpoint still connected).");

    /// <summary>Record one <see cref="_pingSweepTime"/> sample.</summary>
    public void PingSweep(TimeSpan elapsed) =>
        _pingSweepTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>Record one <see cref="_endpointPingTime"/> sample.</summary>
    public void EndpointPing(TimeSpan elapsed) =>
        _endpointPingTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("poolName", poolName));

    /// <summary>
    /// Per-pool reader registry for the <see cref="_poolConnections"/>
    /// pull-mode gauge, keyed by <c>poolName</c>. cppcache uses push
    /// (<c>setCurPoolConnections</c> on every modify); .NET idiomatic pull
    /// lets the listener decide cadence and avoids missing a modify site.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<int>> _poolConnectionsReaders = new();

    /// <summary>
    /// Current number of connections held by the pool. Mirrors cppcache
    /// <c>poolConnections</c> IntGauge (<c>PoolStatistics.cpp:51-52</c>,
    /// <c>m_poolSize</c>).
    /// </summary>
    readonly static ObservableGauge<int> _poolConnections = _meter.CreateObservableGauge(
        "PoolConnections",
        observeValues: ObservePoolConnections,
        unit: "connections",
        description: "Current number of connections held by the pool. Mirrors cppcache `poolConnections` IntGauge (m_poolSize).");

    private static IEnumerable<Measurement<int>> ObservePoolConnections()
    {
        foreach (var (name, reader) in _poolConnectionsReaders)
        {
            yield return new Measurement<int>(reader(), new KeyValuePair<string, object?>("poolName", name));
        }
    }

    /// <summary>Register this pool's reader for the <c>PoolConnections</c> gauge.</summary>
    public void SetPoolConnectionsReader(Func<int> reader) =>
        _poolConnectionsReaders[poolName] = reader;

    /// <summary>Drop this pool's reader from the gauge registry.</summary>
    public void ClearPoolConnectionsReader() =>
        _poolConnectionsReaders.TryRemove(poolName, out _);

    /// <summary>
    /// Per-pool reader registry for the <see cref="_locators"/> pull-mode
    /// gauge. cppcache <c>setLocators</c> push site is single (after
    /// successful <c>getEndpointForNewFwdConn</c>, <c>ThinClientPoolDM.cpp:596</c>);
    /// pull-mode covers both the on-demand path and the background
    /// <c>UpdateLocatorsLocalAsync</c> swap without instrumenting either.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<int>> _locatorsReaders = new();

    /// <summary>
    /// Current number of locators known to the pool's
    /// <see cref="ThinClientLocatorHelper"/>. Mirrors cppcache
    /// <c>locators</c> IntGauge (<c>PoolStatistics.cpp:36-37</c>).
    /// </summary>
    readonly static ObservableGauge<int> _locators = _meter.CreateObservableGauge(
        "Locators",
        observeValues: ObserveLocators,
        unit: "locators",
        description: "Current number of locators known to the pool. Mirrors cppcache `locators` IntGauge.");

    private static IEnumerable<Measurement<int>> ObserveLocators()
    {
        foreach (var (name, reader) in _locatorsReaders)
        {
            yield return new Measurement<int>(reader(), new KeyValuePair<string, object?>("poolName", name));
        }
    }

    /// <summary>Register this pool's reader for the <c>Locators</c> gauge.</summary>
    public void SetLocatorsReader(Func<int> reader) =>
        _locatorsReaders[poolName] = reader;

    /// <summary>Drop this pool's reader from the gauge registry.</summary>
    public void ClearLocatorsReader() =>
        _locatorsReaders.TryRemove(poolName, out _);

    /// <summary>
    /// Per-pool reader registry for the <see cref="_servers"/> pull-mode
    /// gauge. cppcache <c>setServers</c> only fires on <c>addEP</c>
    /// (<c>ThinClientPoolDM.cpp:2019</c>) — never decrements — making it a
    /// monotonic high-water mark in cppcache; pull-mode lets our reader
    /// reflect endpoint removal whenever that lands.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<int>> _serversReaders = new();

    /// <summary>
    /// Current number of endpoints the pool is aware of. Mirrors cppcache
    /// <c>servers</c> IntGauge (<c>PoolStatistics.cpp:38-39</c>).
    /// </summary>
    readonly static ObservableGauge<int> _servers = _meter.CreateObservableGauge(
        "Servers",
        observeValues: ObserveServers,
        unit: "servers",
        description: "Current number of endpoints the pool is aware of. Mirrors cppcache `servers` IntGauge.");

    private static IEnumerable<Measurement<int>> ObserveServers()
    {
        foreach (var (name, reader) in _serversReaders)
        {
            yield return new Measurement<int>(reader(), new KeyValuePair<string, object?>("poolName", name));
        }
    }

    /// <summary>Register this pool's reader for the <c>Servers</c> gauge.</summary>
    public void SetServersReader(Func<int> reader) =>
        _serversReaders[poolName] = reader;

    /// <summary>Drop this pool's reader from the gauge registry.</summary>
    public void ClearServersReader() =>
        _serversReaders.TryRemove(poolName, out _);

    /// <summary>
    /// Per-pool reader registry for the <see cref="_connectedServers"/>
    /// pull-mode gauge. cppcache pushes via <c>incConnectedEndpoints</c> /
    /// <c>decConnectedEndpoints</c> bumping the
    /// <c>connected_endpoints_</c> atomic; pull-mode reads the live
    /// counter on each listener tick.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<int>> _connectedServersReaders = new();

    /// <summary>
    /// Current number of endpoints whose <c>IsConnected</c> bit is true
    /// (i.e. healthy, ping-passing). No direct cppcache catalogue entry —
    /// surfaces the <c>connected_endpoints_</c> atomic
    /// (<c>ThinClientPoolDM.cpp:2055-2068</c>) that cppcache keeps as
    /// internal state for the PDX-registry-clear trigger.
    /// </summary>
    readonly static ObservableGauge<int> _connectedServers = _meter.CreateObservableGauge(
        "ConnectedServers",
        observeValues: ObserveConnectedServers,
        unit: "servers",
        description: "Current number of endpoints whose IsConnected bit is true. Surfaces cppcache `connected_endpoints_` atomic.");

    private static IEnumerable<Measurement<int>> ObserveConnectedServers()
    {
        foreach (var (name, reader) in _connectedServersReaders)
        {
            yield return new Measurement<int>(reader(), new KeyValuePair<string, object?>("poolName", name));
        }
    }

    /// <summary>Register this pool's reader for the <c>ConnectedServers</c> gauge.</summary>
    public void SetConnectedServersReader(Func<int> reader) =>
        _connectedServersReaders[poolName] = reader;

    /// <summary>Drop this pool's reader from the gauge registry.</summary>
    public void ClearConnectedServersReader() =>
        _connectedServersReaders.TryRemove(poolName, out _);

    /// <summary>ActivitySource for traceable RPC spans.</summary>
    readonly static ActivitySource _activitySource = new("Geode.Client.Pool", AssemblyVersion);

    /// <summary>Start an Activity span for a <c>LocatorListRequest</c> RPC.</summary>
    public Activity? StartLocatorListRequest() =>
        _activitySource.StartActivity("LocatorListRequest", ActivityKind.Client)?.SetTag("poolName", poolName);

    /// <summary>Start an Activity span for a <c>ClientConnectionRequest</c> RPC.</summary>
    public Activity? StartClientConnectionRequest() =>
        _activitySource.StartActivity("ClientConnectionRequest", ActivityKind.Client)?.SetTag("poolName", poolName);
}
