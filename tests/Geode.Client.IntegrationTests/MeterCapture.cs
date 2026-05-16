using System.Diagnostics.Metrics;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Test helper that listens on a single named instrument and tracks the
/// number of measurements + running sum + last value. Handles
/// <c>long</c>, <c>double</c>, and <c>int</c> instruments — push (Counter,
/// Histogram) fire on their own; pull (ObservableGauge, ObservableCounter,
/// ObservableUpDownCounter) fire when <see cref="Observe"/> is called.
/// Used to assert <c>PoolStatistics</c> instruments fire on the expected
/// code paths without exposing implementation-side snapshot properties.
/// </summary>
internal sealed class MeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _count;
    private double _sum;
    private double _lastValue;
    private readonly Lock _sumLock = new();

    public MeterCapture(string meterName, string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (_, value, _, _) => Record(value));
        _listener.SetMeasurementEventCallback<double>(
            (_, value, _, _) => Record(value));
        _listener.SetMeasurementEventCallback<int>(
            (_, value, _, _) => Record(value));
        _listener.Start();
    }

    public long Count => Interlocked.Read(ref _count);

    public double Sum
    {
        get
        {
            lock (_sumLock) return _sum;
        }
    }

    /// <summary>
    /// Last measurement value seen. For ObservableGauge instruments this
    /// reflects the most recent <see cref="Observe"/> invocation.
    /// </summary>
    public double LastValue
    {
        get
        {
            lock (_sumLock) return _lastValue;
        }
    }

    /// <summary>
    /// Pull current values from any subscribed Observable* instruments.
    /// Push instruments (Counter, Histogram) ignore this — they fire on
    /// Add / Record at their own call site.
    /// </summary>
    public void Observe() => _listener.RecordObservableInstruments();

    private void Record(double value)
    {
        Interlocked.Increment(ref _count);
        lock (_sumLock)
        {
            _sum += value;
            _lastValue = value;
        }
    }

    public void Dispose() => _listener.Dispose();
}
