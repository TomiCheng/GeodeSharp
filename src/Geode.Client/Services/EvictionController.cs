using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Geode.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

internal sealed class EvictionController(
    SystemProperties systemProperties,
    ILogger<EvictionController> logger)
{

    private long _heapSize;

    private readonly float _heapSizeDelta = systemProperties.HeapLRUDelta / 100.0f;
    private readonly ILogger<EvictionController> _logger = logger;

    private readonly long _maxHeapSize = systemProperties.HeapLRULimit << 20;

    private readonly HashSet<RegionInternal> _regions = [];

    private readonly Lock _regionsLock = new();

    private bool _running;

    private readonly SemaphoreSlim _signal = new(initialCount: 0);

    private Task? _thread;

    private async Task CheckHeapSizeAsync(CancellationToken ct = default)
    {

        var heapSize = Interlocked.Read(ref _heapSize);
        if (heapSize <= _maxHeapSize)
        {
            return;
        }

        var percentage = (float)(heapSize - _maxHeapSize) / _maxHeapSize + _heapSizeDelta;

        _logger.LogDebug(
            "EvictionController::process_delta: evicting {Percentage:F3}% of the entries. Heap size is: {HeapSize} / {MaxHeapSize}",
            percentage * 100.0f, heapSize, _maxHeapSize);

        await EvictAsync(percentage, ct).ConfigureAwait(false);
    }

    private async Task EvictAsync(float percentage, CancellationToken ct = default)
    {
        List<RegionInternal> snapshot;
        lock (_regionsLock)
        {
            snapshot = [.. _regions];
        }

        foreach (var region in snapshot)
        {
            await region.EvictAsync(percentage, ct).ConfigureAwait(false);
        }
    }

    private async Task SvcAsync()
    {
        while (Volatile.Read(ref _running))
        {
            await _signal.WaitAsync().ConfigureAwait(false);
            if (!Volatile.Read(ref _running)) break;
            await CheckHeapSizeAsync().ConfigureAwait(false);
        }
    }

    public void IncrementHeapSize(long delta)
    {
        Interlocked.Add(ref _heapSize, delta);
        _signal.Release();
    }

    public void RegisterRegion(RegionInternal region)
    {
        lock (_regionsLock)
        {
            if (_regions.Add(region))
            {
                _logger.LogDebug(
                    "Registered region with Heap LRU eviction controller: name is {RegionName}",
                    region.FullPath);
            }
        }
    }

    public void Start()
    {
        if (Volatile.Read(ref _running)) return;
        Volatile.Write(ref _running, true);
        _thread = Task.Run(SvcAsync);
        _logger.LogDebug("Eviction Controller started");
    }

    public async Task StopAsync()
    {
        if (!Volatile.Read(ref _running)) return;
        Volatile.Write(ref _running, false);
        _signal.Release();
        if (_thread is not null)
        {
            await _thread.ConfigureAwait(false);
        }
        lock (_regionsLock)
        {
            _regions.Clear();
        }
        _logger.LogDebug("Eviction controller stopped");
    }

    public void UnregisterRegion(RegionInternal region)
    {
        lock (_regionsLock)
        {
            if (_regions.Remove(region))
            {
                _logger.LogDebug(
                    "Deregistered region with Heap LRU eviction controller: name is {RegionName}",
                    region.FullPath);
            }
        }
    }

}
