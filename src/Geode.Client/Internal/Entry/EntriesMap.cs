using Geode.Client.Protocol;

namespace Geode.Client.Internal.Entry;


internal abstract class EntriesMap
    : IAsyncDisposable
{

    public int AddTrackerForEntry(object key, object? oldValue, bool addIfAbsent, bool failIfPresent, bool incUpdateCount)
        => throw new NotImplementedException();
    public abstract Task ClearAsync(CancellationToken ct = default);
    public abstract bool ContainsKey(object key);
    public Task<(MapEntry? Entry, object? OldValue)> CreateAsync(object key, object newValue, int updateCount,
        int destroyTracker,
        VersionTag? versionTag,
        CancellationToken ct = default) => throw new NotImplementedException();
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public abstract (MapEntry? Entry, object? Value) GetEntry(object key);

    public virtual Task<object?> GetFromDiskAsync(object key, MapEntry entry, CancellationToken ct = default)
        => Task.FromResult<object?>(null);
    public virtual void Open(int initialCapacity) { }
    public abstract Task<(MapEntry Entry, object? OldValue, bool IsUpdate)> PutAsync(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null,
        CancellationToken ct = default);

    public abstract Task<(MapEntry? Entry, object? OldValue)> RemoveAsync(
        object key,
        int updateCount,
        VersionTag? versionTag,
        bool afterRemote,
        CancellationToken ct = default);
    public abstract void RemoveTrackerForEntry(object key);

    public abstract int Count { get; }

}
