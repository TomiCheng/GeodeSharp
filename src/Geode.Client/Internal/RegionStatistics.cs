using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Geode.Client.Internal;

/// <summary>
/// Region-scoped Meter sink. Mirrors cppcache <c>RegionStats</c>
/// (<c>RegionStats.{cpp,hpp}</c>) — emits per-region counters / histograms
/// / gauges via <see cref="System.Diagnostics.Metrics"/> so OTel /
/// Prometheus exporters scrape without touching every <c>Region</c>
/// modify site.
/// </summary>
/// <remarks>
/// cppcache <c>RegionStats</c> 25-field catalogue
/// (<c>RegionStats.cpp:36-129</c>): int counter × 16, long counter × 8,
/// int gauge × 1. Mapped here as 8 Counters + 8 Histograms (each
/// <c>*Time</c> long counter collapses with its count counterpart the
/// same way <see cref="PoolStatistics"/> merged <c>connectionWaits</c>
/// into <c>ConnectionWaitTime</c>) + 1 ObservableGauge (<c>entries</c>).
/// cppcache stat-name typo <c>cacheLoaderCallTIme</c>
/// (<c>RegionStats.cpp:98</c>) is corrected to <c>LoaderCallTime</c> here
/// — the typo is the cpp stat name string only, not a wire-protocol
/// constant.
/// </remarks>
internal class RegionStatistics(string regionName)
{
    private static readonly string _assemblyVersion =
        typeof(RegionStatistics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Shared Meter for all region-scoped instruments.</summary>
    private static readonly Meter _meter = new("Geode.Client.Region", _assemblyVersion);

    /// <summary>
    /// Total entry creates for this region. Mirrors cppcache <c>creates</c>
    /// IntCounter (<c>RegionStats.cpp:36-38</c>).
    /// </summary>
    private static readonly Counter<int> _creates = _meter.CreateCounter<int>(
        "Creates",
        unit: "entries",
        description: "Total entry creates for this region. Mirrors cppcache `creates` IntCounter.");

    /// <summary>Bump <see cref="_creates"/>.</summary>
    public void Create() =>
        _creates.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one <c>Put</c> on this region. Mirrors cppcache
    /// <c>putTime</c> LongCounter (ns; <c>RegionStats.cpp:81-83</c>).
    /// <c>.Count</c> subsumes cppcache <c>puts</c> IntCounter
    /// (<c>RegionStats.cpp:39-41</c>) the same way
    /// <c>PoolStatistics.ConnectionWaitTime</c> subsumes
    /// <c>connectionWaits</c>; sum gives total put time, mean gives
    /// average put latency.
    /// </summary>
    private static readonly Histogram<double> _putTime = _meter.CreateHistogram<double>(
        "PutTime",
        unit: "s",
        description: "Elapsed time of one Put on this region. .Count subsumes cppcache `puts` IntCounter.");

    /// <summary>Record one <see cref="_putTime"/> sample.</summary>
    public void Put(TimeSpan elapsed) =>
        _putTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one <c>Get</c> on this region. Mirrors cppcache
    /// <c>getTime</c> LongCounter (ns; <c>RegionStats.cpp:78-80</c>).
    /// <c>.Count</c> subsumes cppcache <c>gets</c> IntCounter
    /// (<c>RegionStats.cpp:42-44</c>).
    /// </summary>
    private static readonly Histogram<double> _getTime = _meter.CreateHistogram<double>(
        "GetTime",
        unit: "s",
        description: "Elapsed time of one Get on this region. .Count subsumes cppcache `gets` IntCounter.");

    /// <summary>Record one <see cref="_getTime"/> sample.</summary>
    public void Get(TimeSpan elapsed) =>
        _getTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total entry-get hits for this region. Mirrors cppcache <c>hits</c>
    /// IntCounter (<c>RegionStats.cpp:45-47</c>).
    /// </summary>
    private static readonly Counter<int> _hits = _meter.CreateCounter<int>(
        "Hits",
        unit: "entries",
        description: "Total entry-get hits for this region. Mirrors cppcache `hits` IntCounter.");

    /// <summary>Bump <see cref="_hits"/>.</summary>
    public void Hit() =>
        _hits.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total entry-get misses for this region. Mirrors cppcache
    /// <c>misses</c> IntCounter (<c>RegionStats.cpp:48-50</c>).
    /// </summary>
    private static readonly Counter<int> _misses = _meter.CreateCounter<int>(
        "Misses",
        unit: "entries",
        description: "Total entry-get misses for this region. Mirrors cppcache `misses` IntCounter.");

    /// <summary>Bump <see cref="_misses"/>.</summary>
    public void Miss() =>
        _misses.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total entry destroys for this region. Mirrors cppcache
    /// <c>destroys</c> IntCounter (<c>RegionStats.cpp:54-56</c>).
    /// </summary>
    private static readonly Counter<int> _destroys = _meter.CreateCounter<int>(
        "Destroys",
        unit: "entries",
        description: "Total entry destroys for this region. Mirrors cppcache `destroys` IntCounter.");

    /// <summary>Bump <see cref="_destroys"/>.</summary>
    public void Destroy() =>
        _destroys.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total entries overflowed to persistence backup for this region.
    /// Mirrors cppcache <c>overflows</c> IntCounter
    /// (<c>RegionStats.cpp:57-61</c>). No call site yet — persistence
    /// backup isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _overflows = _meter.CreateCounter<int>(
        "Overflows",
        unit: "entries",
        description: "Total entries overflowed to persistence backup for this region. Mirrors cppcache `overflows` IntCounter.");

    /// <summary>Bump <see cref="_overflows"/>.</summary>
    public void Overflow() =>
        _overflows.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total entries retrieved from persistence backup into this region.
    /// Mirrors cppcache <c>retrieves</c> IntCounter
    /// (<c>RegionStats.cpp:62-66</c>). No call site yet — persistence
    /// backup isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _retrieves = _meter.CreateCounter<int>(
        "Retrieves",
        unit: "entries",
        description: "Total entries retrieved from persistence backup into this region. Mirrors cppcache `retrieves` IntCounter.");

    /// <summary>Bump <see cref="_retrieves"/>.</summary>
    public void Retrieve() =>
        _retrieves.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total times metadata was refreshed due to single-hop misses on
    /// this region. Mirrors cppcache <c>metaDataRefreshCount</c>
    /// IntCounter (<c>RegionStats.cpp:67-71</c>). Phase 4 (single-hop /
    /// partition routing).
    /// </summary>
    private static readonly Counter<int> _metaDataRefreshCount = _meter.CreateCounter<int>(
        "MetaDataRefreshCount",
        unit: "entries",
        description: "Total times metadata was refreshed due to single-hop misses on this region. Mirrors cppcache `metaDataRefreshCount` IntCounter.");

    /// <summary>Bump <see cref="_metaDataRefreshCount"/>.</summary>
    public void MetaDataRefresh() =>
        _metaDataRefreshCount.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one <c>GetAll</c> on this region. Mirrors cppcache
    /// <c>getAllTime</c> LongCounter (ns; <c>RegionStats.cpp:88-91</c>).
    /// <c>.Count</c> subsumes cppcache <c>getAll</c> IntCounter
    /// (<c>RegionStats.cpp:72-74</c>).
    /// </summary>
    private static readonly Histogram<double> _getAllTime = _meter.CreateHistogram<double>(
        "GetAllTime",
        unit: "s",
        description: "Elapsed time of one GetAll on this region. .Count subsumes cppcache `getAll` IntCounter.");

    /// <summary>Record one <see cref="_getAllTime"/> sample.</summary>
    public void GetAll(TimeSpan elapsed) =>
        _getAllTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one <c>PutAll</c> on this region. Mirrors cppcache
    /// <c>putAllTime</c> LongCounter (ns; <c>RegionStats.cpp:84-87</c>).
    /// <c>.Count</c> subsumes cppcache <c>putAll</c> IntCounter
    /// (<c>RegionStats.cpp:75-77</c>).
    /// </summary>
    private static readonly Histogram<double> _putAllTime = _meter.CreateHistogram<double>(
        "PutAllTime",
        unit: "s",
        description: "Elapsed time of one PutAll on this region. .Count subsumes cppcache `putAll` IntCounter.");

    /// <summary>Record one <see cref="_putAllTime"/> sample.</summary>
    public void PutAll(TimeSpan elapsed) =>
        _putAllTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one <c>RemoveAll</c> on this region. Mirrors
    /// cppcache <c>removeAllTime</c> LongCounter (ns;
    /// <c>RegionStats.cpp:126-129</c>). <c>.Count</c> subsumes cppcache
    /// <c>removeAll</c> IntCounter (<c>RegionStats.cpp:123-125</c>).
    /// </summary>
    private static readonly Histogram<double> _removeAllTime = _meter.CreateHistogram<double>(
        "RemoveAllTime",
        unit: "s",
        description: "Elapsed time of one RemoveAll on this region. .Count subsumes cppcache `removeAll` IntCounter.");

    /// <summary>Record one <see cref="_removeAllTime"/> sample.</summary>
    public void RemoveAll(TimeSpan elapsed) =>
        _removeAllTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one cache-loader invocation for this region.
    /// Mirrors cppcache <c>cacheLoaderCallTIme</c> LongCounter (ns;
    /// <c>RegionStats.cpp:97-100</c>; cpp stat-name typo intentionally
    /// corrected). <c>.Count</c> subsumes cppcache
    /// <c>cacheLoaderCallsCompleted</c> IntCounter
    /// (<c>RegionStats.cpp:93-96</c>). No call site yet — cache loaders
    /// aren't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Histogram<double> _loaderCallTime = _meter.CreateHistogram<double>(
        "LoaderCallTime",
        unit: "s",
        description: "Elapsed time of one cache-loader invocation for this region. .Count subsumes cppcache `cacheLoaderCallsCompleted` IntCounter.");

    /// <summary>Record one <see cref="_loaderCallTime"/> sample.</summary>
    public void LoaderCall(TimeSpan elapsed) =>
        _loaderCallTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one cache-writer invocation for this region.
    /// Mirrors cppcache <c>cacheWriterCallTime</c> LongCounter (ns;
    /// <c>RegionStats.cpp:106-108</c>). <c>.Count</c> subsumes cppcache
    /// <c>cacheWriterCallsCompleted</c> IntCounter
    /// (<c>RegionStats.cpp:101-105</c>). No call site yet — cache writers
    /// aren't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Histogram<double> _writerCallTime = _meter.CreateHistogram<double>(
        "WriterCallTime",
        unit: "s",
        description: "Elapsed time of one cache-writer invocation for this region. .Count subsumes cppcache `cacheWriterCallsCompleted` IntCounter.");

    /// <summary>Record one <see cref="_writerCallTime"/> sample.</summary>
    public void WriterCall(TimeSpan elapsed) =>
        _writerCallTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Elapsed time of one cache-listener invocation for this region.
    /// Mirrors cppcache <c>cacheListenerCallTime</c> LongCounter (ns;
    /// <c>RegionStats.cpp:114-117</c>). <c>.Count</c> subsumes cppcache
    /// <c>cacheListenerCallsCompleted</c> IntCounter
    /// (<c>RegionStats.cpp:109-113</c>). No call site yet — cache
    /// listeners aren't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Histogram<double> _listenerCallTime = _meter.CreateHistogram<double>(
        "ListenerCallTime",
        unit: "s",
        description: "Elapsed time of one cache-listener invocation for this region. .Count subsumes cppcache `cacheListenerCallsCompleted` IntCounter.");

    /// <summary>Record one <see cref="_listenerCallTime"/> sample.</summary>
    public void ListenerCall(TimeSpan elapsed) =>
        _listenerCallTime.Record(elapsed.TotalSeconds, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Total <c>Clear</c> calls on this region. Mirrors cppcache
    /// <c>clears</c> IntCounter (<c>RegionStats.cpp:118-122</c>).
    /// </summary>
    private static readonly Counter<int> _clears = _meter.CreateCounter<int>(
        "Clears",
        unit: "operations",
        description: "Total Clear calls on this region. Mirrors cppcache `clears` IntCounter.");

    /// <summary>Bump <see cref="_clears"/>.</summary>
    public void Clear() =>
        _clears.Add(1, new KeyValuePair<string, object?>("regionName", regionName));

    /// <summary>
    /// Per-region reader registry for the <see cref="_entries"/> pull-mode
    /// gauge, keyed by <c>regionName</c>. cppcache uses push (<c>setEntries</c>
    /// at every modify site, <c>RegionStats.hpp:75-77</c>); .NET idiomatic
    /// pull lets the listener decide cadence and avoids missing a modify
    /// site.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Func<int>> _entriesReaders = new();

    /// <summary>
    /// Current number of entries in this region. Mirrors cppcache
    /// <c>entries</c> IntGauge (<c>RegionStats.cpp:51-53</c>,
    /// <c>setInt(m_entriesId, ...)</c>). No call site yet — local region
    /// entry tracking isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly ObservableGauge<int> _entries = _meter.CreateObservableGauge(
        "Entries",
        observeValues: ObserveEntries,
        unit: "entries",
        description: "Current number of entries in this region. Mirrors cppcache `entries` IntGauge.");

    private static IEnumerable<Measurement<int>> ObserveEntries()
    {
        foreach (var (name, reader) in _entriesReaders)
        {
            yield return new Measurement<int>(reader(), new KeyValuePair<string, object?>("regionName", name));
        }
    }

    /// <summary>Register this region's reader for the <c>Entries</c> gauge.</summary>
    public void SetEntriesReader(Func<int> reader) =>
        _entriesReaders[regionName] = reader;

    /// <summary>Drop this region's reader from the gauge registry.</summary>
    public void ClearEntriesReader() =>
        _entriesReaders.TryRemove(regionName, out _);
}
