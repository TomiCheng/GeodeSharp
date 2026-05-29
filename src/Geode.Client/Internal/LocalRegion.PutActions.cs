using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

partial class LocalRegion
{

    class PutActions(ILogger<PutActions> logger, LocalRegion localRegion,
        object key, object? value, object? callbackArgument, int updateCount,
        CacheEventFlags eventFlags, DataInput? delta = null)
        : IRegionAction
    {

        static readonly ObjectFactory<PutActions> _objectFactory
            = ActivatorUtilities.CreateFactory<PutActions>(
                [typeof(LocalRegion), typeof(object), typeof(object), typeof(object),
                 typeof(int), typeof(CacheEventFlags), typeof(DataInput)]);

        internal static PutActions Create(
            IServiceProvider serviceProvider,
            LocalRegion localRegion,
            object key,
            object? value,
            object? callbackArgument,
            int updateCount,
            CacheEventFlags eventFlags,
            DataInput? delta = null)
            => _objectFactory(serviceProvider,
                [localRegion, key, value, callbackArgument, updateCount, eventFlags, delta]);

        public void CheckArgs()
        {
            ArgumentNullException.ThrowIfNull(key);
            if (value is null && delta is null)
            {
                throw new ArgumentException(
                    "PutActions: value and delta cannot both be null.", nameof(value));
            }
        }
        public void GetCallbackOldValue()
        {
            var cachingEnabled = localRegion.Attributes.CachingEnabled;
            if (cachingEnabled)
            {
                (Entry, OldValue) = localRegion.LocalEntriesMap.Value!.GetEntry(Key);
            }
        }
        public async Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct)
        {
            OldValue = await localRegion
                .PutLocalAsync(
                    name: Name,
                    isCreate: false,
                    key: Key,
                    value: Value,
                    cachingEnabled: localRegion.Attributes.CachingEnabled,
                    updateCount: updateCount,
                    destroyTracker: 0,
                    versionTag: VersionTag,
                    delta: delta,
                    eventId: null,
                    ct: ct)
                .ConfigureAwait(false);
        }
        public void LogCacheWriterFailure()
        {
            logger.LogTrace(
                "Cache writer vetoed {Operation} for key {Key}",
                OldValue is null ? "create" : "update",
                Key);
        }
        public async Task RemoteUpdateAsync(CancellationToken ct)
        {
            VersionTag = await localRegion
                .PutNoThrowRemoteAsync(Key, Value, CallbackArgument, ct: ct)
                .ConfigureAwait(false);
        }

        public bool AddIfAbsent => true;
        public EntryEventType AfterEventType => EntryEventType.AfterUpdate;
        public EntryEventType BeforeEventType => EntryEventType.BeforeUpdate;
        public object? CallbackArgument => callbackArgument;
        public MapEntry? Entry { get; set; }
        public CacheEventFlags EventFlags => eventFlags;
        public bool FailIfPresent => false;
        public object Key => key;
        public string Name => "Region::put";
        public object? OldValue { get; set; }
        public TXState? TxState { get; } = localRegion.GetTXState();
        public int UpdateCount { get; set; } = updateCount;
        public object? Value => value;
        public VersionTag? VersionTag { get; set; }
    }
}
