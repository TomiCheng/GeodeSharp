using System.Diagnostics.Metrics;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Test helper that listens on a single named instrument and aggregates
/// its <c>long</c> measurements. Used to assert <c>PoolStatistics</c>
/// counters fire on the expected code paths without exposing
/// implementation-side snapshot properties.
/// </summary>
internal sealed class MeterCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _value;

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
            (_, value, _, _) => Interlocked.Add(ref _value, value));
        _listener.Start();
    }

    public long Value => Interlocked.Read(ref _value);

    public void Dispose() => _listener.Dispose();
}
