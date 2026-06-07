using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal.Entry;

internal class LruEntriesMap :
    ConcurrentEntriesMap
{

    static readonly ObjectFactory<LruEntriesMap> _objectFactory
    = ActivatorUtilities.CreateFactory<LruEntriesMap>(
        [typeof(EntryFactory), typeof(LocalRegion), typeof(LruAction.Action), typeof(int),
                typeof(bool), typeof(int), typeof(bool)]);


    private readonly Lazy<LruAction> _action;

    private long _currentMapSize;

    private readonly EvictionController? _evictionController;

    private readonly bool _heapLruEnabled;

    private readonly int _limit;

    private readonly ILogger<LruEntriesMap> _logger;

    private readonly LruQueue _lruQueue = new();

    private readonly string _name;

    private IPersistenceManager? _persistenceManager;

    private readonly LocalRegion _region;

    readonly SerializationRegistry _serializationRegistry;

    private int _validEntries;

    public LruEntriesMap(IServiceProvider serviceProvider,
        EntryFactory factory, LocalRegion region, LruAction.Action lruEvictionAction,
        int lruLimit, bool concurrencyChecksEnabled, int concurrency, bool heapLRUEnabled)
        : base(serviceProvider, factory, concurrencyChecksEnabled, region, concurrency)
    {
        _logger = serviceProvider.GetRequiredService<ILogger<LruEntriesMap>>();
        _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();
        _limit = lruLimit;
        _region = region;
        _name = region.FullPath;
        _heapLruEnabled = heapLRUEnabled;
        _action = new Lazy<LruAction>(() => LruAction.NewLruAction(serviceProvider, lruEvictionAction, region, this));
        if (_heapLruEnabled)
        {
            _evictionController = serviceProvider.GetRequiredService<EvictionController>();
            _evictionController.RegisterRegion(_region);
            _logger.LogInformation(
                "Heap LRU eviction controller registered region {RegionName}",
                _name);
        }

    }

    private async Task<bool> EvictionHelperAsync(CancellationToken ct = default)
    {
        var entry = _lruQueue.Pop();
        if (entry is null)
        {
            return false;   // GF_ENOENT: nothing to evict.
        }

        // m_action->evict(entry): e.g. LOCAL_DESTROY → region.DestroyNoThrowAsync.
        var evictDone = await _action.Value.EvictAsync(entry, ct).ConfigureAwait(false);

        // overflow-only valid-count adjust (deferred path).
        if (_action.Value.Overflows && evictDone)
        {
            --_validEntries;
        }

        // GF_DISKFULL: overflow write failed → stop.
        return evictDone;
    }

    private bool MustEvict()
    {
        if (_action.Value.Overflows)
        {
            return ValidEntriesSize() > _limit;
        }
        if (_heapLruEnabled && _limit == 0)
        {
            return false;
        }
        return Count > _limit;
    }

    private async Task ProcessLruAsync(CancellationToken ct = default)
    {
        while (MustEvict())
        {
            await EvictionHelperAsync(ct).ConfigureAwait(false);
        }
    }

    private int ValidEntriesSize() => _validEntries;

    internal async Task ProcessLruAsync(int numEntriesToEvict, CancellationToken ct = default)
    {
        for (var i = 0; i < numEntriesToEvict; i++)
        {
            if (_validEntries <= 0 || Count <= 0)
            {
                break;
            }
            await EvictionHelperAsync(ct).ConfigureAwait(false);
        }
    }

    internal void SetPersistenceManager(IPersistenceManager pmPtr) => _persistenceManager = pmPtr;

    internal void UpdateMapSize(long size)
    {
        if (_evictionController is null)
        {
            return;
        }

        Interlocked.Add(ref _currentMapSize, size);
        _evictionController.IncrementHeapSize(size);
    }

    public override async Task ClearAsync(CancellationToken ct = default)
    {
        if (_evictionController is not null)
        {
            UpdateMapSize(-_currentMapSize);
        }

        // Wipe the backing map + logical size (ConcurrentEntriesMap.ClearAsync).
        await base.ClearAsync(ct).ConfigureAwait(false);
    }

    public static LruEntriesMap? Create(IServiceProvider serviceProvider, EntryFactory factory,
        LocalRegion region, LruAction.Action action, int lruLimit, bool concurrencyChecksEnabled,
        int concurrency, bool heapLRUEnabled)
        => _objectFactory(serviceProvider, [factory, region, action, lruLimit,
            concurrencyChecksEnabled, concurrency, heapLRUEnabled]);

    public override async ValueTask DisposeAsync()
    {
        if (_evictionController is not null)
        {
            _evictionController.IncrementHeapSize(-_currentMapSize);
            _evictionController.UnregisterRegion(_region);
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override async Task<(MapEntry? Entry, object? Value)> GetAsync(object key,
        CancellationToken ct = default)
    {
        var (entry, value) = GetEntry(key);
        if (entry is null)
        {
            return (entry, value);
        }


        if (!CacheableToken.IsOverflowed(value))
        {
            _lruQueue.MoveToEnd(entry);
            return (entry, value);
        }

        var lruProps = ((ILruEntryProperties)entry).LruProperties;
        object? readValue;
        try
        {
            readValue = await _persistenceManager!
                .ReadAsync(key, lruProps.PersistenceInfo, ct).ConfigureAwait(false);
        }
        catch (GeodeException ex)
        {
            _logger.LogError(ex, "read on the persistence layer failed for key {Key}", key);
            return (null, null);
        }

        _region.RegionStats.Retrieve();
        _region.CachePerfStats.Retrieve();

        (entry, _, _) = await base
            .PutAsync(key, readValue!, updateCount: 0, destroyTracker: 0, versionTag: null, ct: ct)
            .ConfigureAwait(false);

        ++_validEntries;
        _lruQueue.Push(entry);

        if (_evictionController is not null)
        {
            var newSize = _serializationRegistry.CheckAndGetObjectSize(readValue)
                        - CacheableToken.Overflowed.ObjectSize;
            UpdateMapSize(newSize);
        }

        await ProcessLruAsync(ct).ConfigureAwait(false);
        return (entry, readValue);
    }

    public override async Task<(MapEntry Entry, object? OldValue, bool IsUpdate)> PutAsync(
        object key, object newValue, int updateCount, int destroyTracker, VersionTag? versionTag,
        DataInput? delta = null,
        CancellationToken ct = default)
    {
        MapEntry entry;
        object? oldValue;
        bool isUpdate;
        {
            (entry, oldValue, isUpdate) = await base.PutAsync(key, newValue, updateCount, destroyTracker, versionTag, delta, ct).ConfigureAwait(false);

            bool isOldValueToken = CacheableToken.IsToken(oldValue);
            if (CacheableToken.IsOverflowed(oldValue))
            {
                var persistenceInfo = ((ILruEntryProperties)entry).LruProperties.PersistenceInfo;
                oldValue = await _persistenceManager!.ReadAsync(key, persistenceInfo, ct).ConfigureAwait(false);
                if (oldValue is not null)
                {
                    await _persistenceManager!.DestroyAsync(key, persistenceInfo, ct).ConfigureAwait(false);
                }
            }

            // TODO:  when can newValue be a token ??
            if (CacheableToken.IsToken(newValue) && !isOldValueToken)
            {
                --_validEntries;
            }
            if (!CacheableToken.IsToken(newValue) && isOldValueToken)
            {
                ++_validEntries;
            }


            // Add new entry to LRU list
            if (isUpdate == false)
            {
                ++_validEntries;
                var (mePtr, _) = GetEntry(key);
                _lruQueue.Push(mePtr!);
                entry = mePtr!;
            }
            else
            {
                if (!CacheableToken.IsToken(newValue) && isOldValueToken)
                {
                    var (mePtr, _) = GetEntry(key);
                    _lruQueue.Push(mePtr!);
                    entry = mePtr!;
                }
            }
        }
        if (_evictionController is not null)
        {
            var newSize = _serializationRegistry.CheckAndGetObjectSize(newValue);

            if (isUpdate == false)
            {
                newSize += _serializationRegistry.CheckAndGetObjectSize(key);
            }
            else
            {
                newSize -= _serializationRegistry.CheckAndGetObjectSize(oldValue);

            }
            UpdateMapSize(newSize);
        }

        await ProcessLruAsync(ct).ConfigureAwait(false);
        return (entry, oldValue, isUpdate);
    }

    public override async Task<(MapEntry? Entry, object? OldValue)> RemoveAsync(
        object key, int updateCount, VersionTag? versionTag, bool afterRemote,
        CancellationToken ct = default)
    {
        var (entry, oldValue) = await base
            .RemoveAsync(key, updateCount, versionTag, afterRemote, ct)
            .ConfigureAwait(false);

        if (_evictionController is not null && entry is not null && oldValue is not null)
        {
            _lruQueue.Remove(entry);
            if (!CacheableToken.IsToken(oldValue))
            {
                --_validEntries;
            }

            var sizeToRemove = _serializationRegistry.CheckAndGetObjectSize(key)
                             + _serializationRegistry.CheckAndGetObjectSize(oldValue);
            UpdateMapSize(-sizeToRemove);
        }

        return (entry, oldValue);
    }

}
