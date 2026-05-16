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


    // Activity
    readonly static ActivitySource _activitySource = new ("Geode.Client.Pool", AssemblyVersion);

    public Activity? StartLocatorListRequest() =>
        _activitySource.StartActivity("LocatorListRequest", ActivityKind.Client)?.SetTag("poolName", poolName);

    public Activity? StartClientConnectionRequest() =>
        _activitySource.StartActivity("ClientConnectionRequest", ActivityKind.Client)?.SetTag("poolName", poolName);




}
