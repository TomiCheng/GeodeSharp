using System.Diagnostics.Metrics;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Test helper that listens on a single named instrument and tracks the
/// number of measurements + their running sum. Handles both <c>long</c>
/// and <c>double</c> instruments. Used to assert <c>PoolStatistics</c>
/// counters / histograms fire on the expected code paths without
/// exposing implementation-side snapshot properties.
/// </summary>
internal sealed class MeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _count;
    private double _sum;
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

    private void Record(double value)
    {
        Interlocked.Increment(ref _count);
        lock (_sumLock) _sum += value;
    }

    public void Dispose() => _listener.Dispose();
}
