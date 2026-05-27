using System.Diagnostics.Metrics;

namespace Geode.Client.Internal;

/// <summary>
/// Cache-wide Meter sink. Mirrors cppcache <c>CachePerfStats</c>
/// (<c>CachePerfStats.hpp</c>) — emits the per-cache 24-field catalogue
/// via <see cref="System.Diagnostics.Metrics"/> so OTel / Prometheus
/// exporters scrape without touching every <c>GeodeCache</c> /
/// <c>Region</c> modify site.
/// </summary>
/// <remarks>
/// cppcache <c>CachePerfStats</c> 24-field catalogue
/// (<c>CachePerfStats.hpp:47-138</c>): int counter × 16, long counter × 6,
/// int gauge × 1, plus two of those int counters (<c>tombstoneCount</c> /
/// <c>nonReplicatedTombstonesSize</c>) used as inc/dec gauges.
/// Mapped here as 13 Counters + 4 Histograms (collapsing four count/time
/// or count/bytes pairs the same way <see cref="PoolStatistics"/> merged
/// <c>connectionWaits</c> into <c>ConnectionWaitTime</c>) + 1
/// ObservableGauge (<c>entries</c>) + 2 UpDownCounters
/// (<c>tombstoneCount</c> / <c>tombstoneSize</c> — cppcache types them as
/// counters but uses <c>incInt(id, +1)</c> / <c>incInt(id, -1)</c>, which
/// is UpDownCounter semantics). cppcache <c>CachePerfStats</c> is
/// singleton-per-cache (<c>findFirstStatisticsByType("CachePerfStats")</c>);
/// we follow suit and don't tag with a <c>cacheName</c> until a real
/// multi-cache observability need surfaces.
/// </remarks>
internal class CachePerfStatistics
{
    private static readonly string _assemblyVersion =
        typeof(CachePerfStatistics).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Shared Meter for all cache-scoped instruments.</summary>
    private static readonly Meter _meter = new("Geode.Client.Cache", _assemblyVersion);

    /// <summary>
    /// Total entry creates across the cache. Mirrors cppcache <c>creates</c>
    /// IntCounter (<c>CachePerfStats.hpp:47-49</c>).
    /// </summary>
    private static readonly Counter<int> _creates = _meter.CreateCounter<int>(
        "Creates",
        unit: "entries",
        description: "Total entry creates across the cache. Mirrors cppcache `creates` IntCounter.");

    /// <summary>Bump <see cref="_creates"/>.</summary>
    public void Create() => _creates.Add(1);

    /// <summary>
    /// Total entry puts across the cache. Mirrors cppcache <c>puts</c>
    /// IntCounter (<c>CachePerfStats.hpp:50-51</c>).
    /// </summary>
    private static readonly Counter<int> _puts = _meter.CreateCounter<int>(
        "Puts",
        unit: "entries",
        description: "Total entry puts across the cache. Mirrors cppcache `puts` IntCounter.");

    /// <summary>Bump <see cref="_puts"/>.</summary>
    public void Put() => _puts.Add(1);

    /// <summary>
    /// Total entry gets across the cache. Mirrors cppcache <c>gets</c>
    /// IntCounter (<c>CachePerfStats.hpp:52-53</c>).
    /// </summary>
    private static readonly Counter<int> _gets = _meter.CreateCounter<int>(
        "Gets",
        unit: "entries",
        description: "Total entry gets across the cache. Mirrors cppcache `gets` IntCounter.");

    /// <summary>Bump <see cref="_gets"/>.</summary>
    public void Get() => _gets.Add(1);

    /// <summary>
    /// Total entry-get hits (server returned a value). Mirrors cppcache
    /// <c>hits</c> IntCounter (<c>CachePerfStats.hpp:54-55</c>).
    /// </summary>
    private static readonly Counter<int> _hits = _meter.CreateCounter<int>(
        "Hits",
        unit: "entries",
        description: "Total entry-get hits. Mirrors cppcache `hits` IntCounter.");

    /// <summary>Bump <see cref="_hits"/>.</summary>
    public void Hit() => _hits.Add(1);

    /// <summary>
    /// Total entry-get misses (server returned no value / not found). Mirrors
    /// cppcache <c>misses</c> IntCounter (<c>CachePerfStats.hpp:56-58</c>).
    /// </summary>
    private static readonly Counter<int> _misses = _meter.CreateCounter<int>(
        "Misses",
        unit: "entries",
        description: "Total entry-get misses. Mirrors cppcache `misses` IntCounter.");

    /// <summary>Bump <see cref="_misses"/>.</summary>
    public void Miss() => _misses.Add(1);

    /// <summary>
    /// Total entry destroys across the cache. Mirrors cppcache <c>destroys</c>
    /// IntCounter (<c>CachePerfStats.hpp:62-64</c>).
    /// </summary>
    private static readonly Counter<int> _destroys = _meter.CreateCounter<int>(
        "Destroys",
        unit: "entries",
        description: "Total entry destroys across the cache. Mirrors cppcache `destroys` IntCounter.");

    /// <summary>Bump <see cref="_destroys"/>.</summary>
    public void Destroy() => _destroys.Add(1);

    /// <summary>
    /// Total entries overflowed to persistence backup. Mirrors cppcache
    /// <c>overflows</c> IntCounter (<c>CachePerfStats.hpp:65-68</c>). No
    /// call site yet — persistence backup isn't implemented; kept for
    /// catalogue parity.
    /// </summary>
    private static readonly Counter<int> _overflows = _meter.CreateCounter<int>(
        "Overflows",
        unit: "entries",
        description: "Total entries overflowed to persistence backup. Mirrors cppcache `overflows` IntCounter.");

    /// <summary>Bump <see cref="_overflows"/>.</summary>
    public void Overflow() => _overflows.Add(1);

    /// <summary>
    /// Total entries retrieved from persistence backup into the cache.
    /// Mirrors cppcache <c>retrieves</c> IntCounter
    /// (<c>CachePerfStats.hpp:69-73</c>). No call site yet — persistence
    /// backup isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _retrieves = _meter.CreateCounter<int>(
        "Retrieves",
        unit: "entries",
        description: "Total entries retrieved from persistence backup. Mirrors cppcache `retrieves` IntCounter.");

    /// <summary>Bump <see cref="_retrieves"/>.</summary>
    public void Retrieve() => _retrieves.Add(1);

    /// <summary>
    /// Total cache-listener invocations that completed. Mirrors cppcache
    /// <c>cacheListenerCallsCompleted</c> IntCounter
    /// (<c>CachePerfStats.hpp:74-77</c>). No call site yet — cache
    /// listeners aren't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _cacheListenerCallsCompleted = _meter.CreateCounter<int>(
        "CacheListenerCallsCompleted",
        unit: "operations",
        description: "Total cache-listener invocations that completed. Mirrors cppcache `cacheListenerCallsCompleted` IntCounter.");

    /// <summary>Bump <see cref="_cacheListenerCallsCompleted"/>.</summary>
    public void CacheListenerCallCompleted() => _cacheListenerCallsCompleted.Add(1);

    /// <summary>
    /// Total puts containing delta sent client → server. Mirrors cppcache
    /// <c>deltaPuts</c> IntCounter (<c>CachePerfStats.hpp:78-82</c>). No
    /// call site yet — delta propagation isn't implemented; kept for
    /// catalogue parity.
    /// </summary>
    private static readonly Counter<int> _deltaPuts = _meter.CreateCounter<int>(
        "DeltaPuts",
        unit: "entries",
        description: "Total puts containing delta sent client→server. Mirrors cppcache `deltaPuts` IntCounter.");

    /// <summary>Bump <see cref="_deltaPuts"/>.</summary>
    public void DeltaPut() => _deltaPuts.Add(1);

    /// <summary>
    /// Total messages containing delta received from server that failed
    /// to apply. Mirrors cppcache <c>deltaMessageFailures</c> IntCounter
    /// (<c>CachePerfStats.hpp:88-92</c>). No call site yet — delta
    /// propagation isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _deltaMessageFailures = _meter.CreateCounter<int>(
        "DeltaMessageFailures",
        unit: "entries",
        description: "Total delta messages received from server that failed to apply. Mirrors cppcache `deltaMessageFailures` IntCounter.");

    /// <summary>Bump <see cref="_deltaMessageFailures"/>.</summary>
    public void DeltaMessageFailure() => _deltaMessageFailures.Add(1);

    /// <summary>
    /// Elapsed time spent applying one delta message received from server.
    /// Mirrors cppcache <c>processedDeltaMessagesTime</c> LongCounter (ns;
    /// <c>CachePerfStats.hpp:93-97</c>). <c>.Count</c> subsumes cppcache
    /// <c>processedDeltaMessages</c> IntCounter
    /// (<c>CachePerfStats.hpp:83-87</c>) the same way
    /// <c>PoolStatistics.ConnectionWaitTime</c> subsumes
    /// <c>connectionWaits</c>; sum gives total delta-apply time, mean gives
    /// average apply latency. No call site yet — delta propagation isn't
    /// implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Histogram<double> _processedDeltaMessageTime = _meter.CreateHistogram<double>(
        "ProcessedDeltaMessageTime",
        unit: "s",
        description: "Elapsed time spent applying one delta message received from server. .Count subsumes cppcache `processedDeltaMessages` IntCounter.");

    /// <summary>Record one <see cref="_processedDeltaMessageTime"/> sample.</summary>
    public void ProcessedDeltaMessage(TimeSpan elapsed) =>
        _processedDeltaMessageTime.Record(elapsed.TotalSeconds);

    /// <summary>
    /// Current number of tombstones the client knows about. cppcache
    /// <c>tombstoneCount</c> (<c>CachePerfStats.hpp:98-100</c>) is typed
    /// IntCounter but used as a gauge via
    /// <c>incTombstoneCount</c>/<c>decTombstoneCount</c> (<c>:249-254</c>);
    /// modelled here as an UpDownCounter. No call site yet — tombstone
    /// tracking isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly UpDownCounter<int> _tombstoneCount = _meter.CreateUpDownCounter<int>(
        "TombstoneCount",
        unit: "entries",
        description: "Current number of tombstones the client knows about. Mirrors cppcache `tombstoneCount` (used as inc/dec gauge).");

    /// <summary>Bump <see cref="_tombstoneCount"/> by +1.</summary>
    public void TombstoneAdded() => _tombstoneCount.Add(1);

    /// <summary>Bump <see cref="_tombstoneCount"/> by -1.</summary>
    public void TombstoneRemoved() => _tombstoneCount.Add(-1);

    /// <summary>
    /// Current bytes consumed by tombstones across all regions. cppcache
    /// <c>nonReplicatedTombstonesSize</c> (<c>CachePerfStats.hpp:101-105</c>)
    /// is typed LongCounter but used as a gauge via
    /// <c>incTombstoneSize(+n)</c>/<c>decTombstoneSize(-n)</c>
    /// (<c>:255-260</c>); modelled here as an UpDownCounter. No call site
    /// yet — tombstone tracking isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly UpDownCounter<long> _tombstoneSize = _meter.CreateUpDownCounter<long>(
        "NonReplicatedTombstonesSize",
        unit: "bytes",
        description: "Current bytes consumed by tombstones. Mirrors cppcache `nonReplicatedTombstonesSize` (used as inc/dec gauge).");

    /// <summary>Bump <see cref="_tombstoneSize"/> by <paramref name="bytes"/>.</summary>
    public void TombstoneSizeAdded(long bytes) => _tombstoneSize.Add(bytes);

    /// <summary>Bump <see cref="_tombstoneSize"/> by <c>-bytes</c>.</summary>
    public void TombstoneSizeRemoved(long bytes) => _tombstoneSize.Add(-bytes);

    /// <summary>
    /// Total conflicting events elided rather than dispatched to listeners.
    /// Mirrors cppcache <c>conflatedEvents</c> IntCounter
    /// (<c>CachePerfStats.hpp:106-110</c>). No call site yet — event
    /// conflation isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly Counter<int> _conflatedEvents = _meter.CreateCounter<int>(
        "ConflatedEvents",
        unit: "operations",
        description: "Total conflicting events elided rather than dispatched to listeners. Mirrors cppcache `conflatedEvents` IntCounter.");

    /// <summary>Bump <see cref="_conflatedEvents"/>.</summary>
    public void ConflatedEvent() => _conflatedEvents.Add(1);

    /// <summary>
    /// Elapsed time of one PdxInstance <c>GetObject</c> deserialization.
    /// Mirrors cppcache <c>pdxInstanceDeserializationTime</c> LongCounter
    /// (ns; <c>CachePerfStats.hpp:115-120</c>). <c>.Count</c> subsumes
    /// cppcache <c>pdxInstanceDeserializations</c> IntCounter
    /// (<c>CachePerfStats.hpp:111-114</c>); sum gives total deserialization
    /// time, mean gives average latency. Phase 2 (PDX).
    /// </summary>
    private static readonly Histogram<double> _pdxInstanceDeserializationTime = _meter.CreateHistogram<double>(
        "PdxInstanceDeserializationTime",
        unit: "s",
        description: "Elapsed time of one PdxInstance GetObject deserialization. .Count subsumes cppcache `pdxInstanceDeserializations` IntCounter.");

    /// <summary>Record one <see cref="_pdxInstanceDeserializationTime"/> sample.</summary>
    public void PdxInstanceDeserialization(TimeSpan elapsed) =>
        _pdxInstanceDeserializationTime.Record(elapsed.TotalSeconds);

    /// <summary>
    /// Total times a deserialization created a PdxInstance (rather than
    /// reifying the full domain object). Mirrors cppcache
    /// <c>pdxInstanceCreations</c> IntCounter
    /// (<c>CachePerfStats.hpp:121-124</c>). Phase 2 (PDX).
    /// </summary>
    private static readonly Counter<int> _pdxInstanceCreations = _meter.CreateCounter<int>(
        "PdxInstanceCreations",
        unit: "entries",
        description: "Total times a deserialization created a PdxInstance. Mirrors cppcache `pdxInstanceCreations` IntCounter.");

    /// <summary>Bump <see cref="_pdxInstanceCreations"/>.</summary>
    public void PdxInstanceCreation() => _pdxInstanceCreations.Add(1);

    /// <summary>
    /// Bytes produced by one PDX serialization. Mirrors cppcache
    /// <c>pdxSerializedBytes</c> LongCounter
    /// (<c>CachePerfStats.hpp:128-131</c>). <c>.Count</c> subsumes cppcache
    /// <c>pdxSerializations</c> IntCounter (<c>CachePerfStats.hpp:125-127</c>);
    /// <c>.Sum</c> gives total bytes written. cppcache's single
    /// <c>incPdxSerialization(bytes)</c> (<c>:300-303</c>) bumps both
    /// fields in one call — collapsed here into one
    /// <see cref="Histogram{T}"/> sample. Phase 2 (PDX).
    /// </summary>
    private static readonly Histogram<long> _pdxSerializedBytes = _meter.CreateHistogram<long>(
        "PdxSerializedBytes",
        unit: "bytes",
        description: "Bytes produced by one PDX serialization. .Count subsumes cppcache `pdxSerializations`; .Sum subsumes cppcache `pdxSerializedBytes`.");

    /// <summary>Record one <see cref="_pdxSerializedBytes"/> sample.</summary>
    public void PdxSerialization(long bytes) => _pdxSerializedBytes.Record(bytes);

    /// <summary>
    /// Bytes read by one PDX deserialization. Mirrors cppcache
    /// <c>pdxDeserializedBytes</c> LongCounter
    /// (<c>CachePerfStats.hpp:135-138</c>). <c>.Count</c> subsumes cppcache
    /// <c>pdxDeserializations</c> IntCounter (<c>CachePerfStats.hpp:132-134</c>);
    /// <c>.Sum</c> gives total bytes read. cppcache's single
    /// <c>incPdxDeSerialization(bytes)</c> (<c>:313-316</c>) bumps both
    /// fields in one call — collapsed here into one
    /// <see cref="Histogram{T}"/> sample. Phase 2 (PDX).
    /// </summary>
    private static readonly Histogram<long> _pdxDeserializedBytes = _meter.CreateHistogram<long>(
        "PdxDeserializedBytes",
        unit: "bytes",
        description: "Bytes read by one PDX deserialization. .Count subsumes cppcache `pdxDeserializations`; .Sum subsumes cppcache `pdxDeserializedBytes`.");

    /// <summary>Record one <see cref="_pdxDeserializedBytes"/> sample.</summary>
    public void PdxDeserialization(long bytes) => _pdxDeserializedBytes.Record(bytes);

    /// <summary>
    /// Reader for the <see cref="_entries"/> pull-mode gauge. cppcache
    /// pushes via <c>incEntries(delta)</c> against a fixed-id
    /// <c>m_entriesId</c> (<c>CachePerfStats.hpp:227-229</c>); pull-mode
    /// lets the listener decide cadence and lets the source of truth
    /// (region maps) report whatever it actually holds.
    /// </summary>
    private static Func<int>? _entriesReader;

    /// <summary>
    /// Current number of cache entries across all regions. Mirrors cppcache
    /// <c>entries</c> IntGauge (<c>CachePerfStats.hpp:59-61</c>,
    /// <c>setInt(m_entriesId, ...)</c>). No call site yet — local region
    /// entry tracking isn't implemented; kept for catalogue parity.
    /// </summary>
    private static readonly ObservableGauge<int> _entries = _meter.CreateObservableGauge(
        "Entries",
        observeValue: () => _entriesReader?.Invoke() ?? 0,
        unit: "entries",
        description: "Current number of cache entries across all regions. Mirrors cppcache `entries` IntGauge.");

    /// <summary>Register the reader for the <c>Entries</c> gauge.</summary>
    public void SetEntriesReader(Func<int> reader) => _entriesReader = reader;

    /// <summary>Drop the reader from the <c>Entries</c> gauge.</summary>
    public void ClearEntriesReader() => _entriesReader = null;
}
