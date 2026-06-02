using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Geode.Client.Tests.Eviction;

/// <summary>
/// Test fake for <see cref="IPersistenceManager"/> — backs overflow-to-disk
/// with an in-memory <see cref="ConcurrentDictionary{TKey, TValue}"/> instead
/// of real disk I/O. Lets the overflow eviction path be exercised in unit
/// tests (write on evict → token in memory → read back on get). <b>Not a
/// production impl</b> — values stay on the heap, so it provides no real
/// memory relief; it only validates the wire-up.
/// </summary>
internal sealed class InMemoryPersistenceManager : IPersistenceManager
{
    /// <summary>key → spilled value. The "disk".</summary>
    public ConcurrentDictionary<object, object?> Store { get; } = new();

    public int WriteCount;
    public int ReadCount;
    public int DestroyCount;

    public ValueTask InitAsync(IRegion region, IReadOnlyDictionary<string, string>? diskProperties, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public ValueTask<object?> WriteAsync(object key, object? value, object? persistenceInfo, CancellationToken ct = default)
    {
        Interlocked.Increment(ref WriteCount);
        Store[key] = value;
        return ValueTask.FromResult<object?>(key);   // handle = the key itself
    }

    public ValueTask<bool> WriteAllAsync(CancellationToken ct = default) => ValueTask.FromResult(true);

    public ValueTask<object?> ReadAsync(object key, object? persistenceInfo, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ReadCount);
        Store.TryGetValue(key, out var value);
        return ValueTask.FromResult(value);
    }

    public ValueTask<bool> ReadAllAsync(CancellationToken ct = default) => ValueTask.FromResult(true);

    public ValueTask DestroyAsync(object key, object? persistenceInfo, CancellationToken ct = default)
    {
        Interlocked.Increment(ref DestroyCount);
        Store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
