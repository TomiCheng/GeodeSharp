using Geode.Client.Protocol;

namespace Geode.Client.Internal.Entry;


internal abstract class EntriesMap
    : IAsyncDisposable
{

    public int AddTrackerForEntry(object key, object? oldValue, bool addIfAbsent, bool failIfPresent, bool incUpdateCount)
        => throw new NotImplementedException();
    public abstract Task ClearAsync(CancellationToken ct = default);
    public abstract bool ContainsKey(object key);
    public abstract Task<(MapEntry? Entry, object? OldValue)> CreateAsync(object key, object newValue, int updateCount,
        int destroyTracker,
        VersionTag? versionTag,
        CancellationToken ct = default);
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public abstract (MapEntry? Entry, object? Value) GetEntry(object key);

    /// <summary>
    /// User-facing read: entry + value for <paramref name="key"/>, applying any
    /// read-back the concrete map needs. Base delegates to <see cref="GetEntry"/>
    /// (cppcache <c>ConcurrentEntriesMap::get</c> == <c>segmentFor-&gt;getEntry</c>);
    /// <see cref="LruEntriesMap"/> overrides it for overflow read-back. cppcache's
    /// GET path (<c>LocalRegion.cpp:892</c>) calls <c>get</c>, NOT <c>getEntry</c> —
    /// put/destroy machinery keeps using the raw <see cref="GetEntry"/>.
    /// </summary>
    public virtual Task<(MapEntry? Entry, object? Value)> GetAsync(object key, CancellationToken ct = default)
        => Task.FromResult(GetEntry(key));

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

    public abstract Task<(MapEntry? Entry, object? OldValue)> InvalidateAsync(
        object key,
        VersionTag? versionTag = null,
        CancellationToken ct = default);

    public abstract void RemoveTrackerForEntry(object key);

    public abstract int Count { get; }

}
