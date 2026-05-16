using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text;

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

    readonly static Meter _meter = new ("Geode.Client.Pool");
    readonly static Counter<long> _locatorRequestsCounter = _meter.CreateCounter<long>("LocatorRequests");

    public void LocatorRequest()
    {
        _locatorRequestsCounter.Add(1,
            new KeyValuePair<string, object?>("poolName", poolName));
    }
}
