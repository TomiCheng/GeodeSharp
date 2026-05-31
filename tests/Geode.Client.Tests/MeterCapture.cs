using System.Diagnostics.Metrics;

namespace Geode.Client.Tests;

/// <summary>
/// Listens on a single named instrument and counts measurements + running
/// sum. Optionally filters to measurements carrying a specific tag
/// (key + value) — needed because the <c>Geode.Client.*</c> instruments are
/// static (shared across every region / cache / test), so an unfiltered
/// listener would see cross-talk from tests running in parallel. Pass a
/// unique <c>regionName</c> / <c>cacheName</c> tag to isolate.
/// </summary>
internal sealed class MeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly string? _tagKey;
    private readonly string? _tagValue;
    private long _count;
    private double _sum;
    private readonly Lock _lock = new();

    public MeterCapture(string meterName, string instrumentName, string? tagKey = null, string? tagValue = null)
    {
        _tagKey = tagKey;
        _tagValue = tagValue;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, v, tags, _) => Record(v, tags));
        _listener.SetMeasurementEventCallback<double>((_, v, tags, _) => Record(v, tags));
        _listener.SetMeasurementEventCallback<int>((_, v, tags, _) => Record(v, tags));
        _listener.Start();
    }

    public long Count
    {
        get { lock (_lock) return _count; }
    }

    public double Sum
    {
        get { lock (_lock) return _sum; }
    }

    private void Record(double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (_tagKey is not null && !TagMatches(tags))
        {
            return;
        }
        lock (_lock)
        {
            _count++;
            _sum += value;
        }
    }

    private bool TagMatches(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == _tagKey && Equals(tag.Value, _tagValue))
            {
                return true;
            }
        }
        return false;
    }

    public void Dispose() => _listener.Dispose();
}
