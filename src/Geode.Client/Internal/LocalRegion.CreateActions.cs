using Geode.Client.Internal.Entry;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

partial class LocalRegion
{

    class CreateActions(ILogger<CreateActions> logger, LocalRegion localRegion,
        object key, object? value, object? callbackArgument, int updateCount,
        CacheEventFlags eventFlags)
        : IRegionAction
    {

        static readonly ObjectFactory<CreateActions> _objectFactory
            = ActivatorUtilities.CreateFactory<CreateActions>(
                [typeof(LocalRegion), typeof(object), typeof(object), typeof(object),
                 typeof(int), typeof(CacheEventFlags)]);

        internal static CreateActions Create(
            IServiceProvider serviceProvider,
            LocalRegion localRegion,
            object key,
            object? value,
            object? callbackArgument,
            int updateCount,
            CacheEventFlags eventFlags)
            => _objectFactory(serviceProvider,
                [localRegion, key, value, callbackArgument, updateCount, eventFlags]);

        public void CheckArgs()
        {
            ArgumentNullException.ThrowIfNull(key);
        }
        public void GetCallbackOldValue()
        {
        }
        public async Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct)
        {
            await localRegion.PutLocalAsync(Name, true, key, value,
                    cachingEnabled: localRegion.Attributes.CachingEnabled,
                    updateCount, 0, VersionTag, null, null, ct).ConfigureAwait(false);
        }
        public void LogCacheWriterFailure()
        { 
            logger.LogTrace("Cache writer vetoed create for key {Key}", Key);
        }
        public async Task RemoteUpdateAsync(CancellationToken ct)
        {
            VersionTag = await localRegion.CreateNoThrowRemoteAsync(key, value, callbackArgument, ct);
        }

        public bool AddIfAbsent => true;
        public EntryEventType AfterEventType => EntryEventType.AfterCreate;
        public EntryEventType BeforeEventType => EntryEventType.BeforeCreate;
        public object? CallbackArgument => callbackArgument;
        public MapEntry? Entry { get; set; }
        public CacheEventFlags EventFlags => eventFlags;
        public bool FailIfPresent => true;
        public object Key => key;
        public string Name => "Region::create";
        public object? OldValue { get; set; }
        public TXState? TxState { get; } = localRegion.GetTXState();
        public int UpdateCount { get; set; } = updateCount;
        public object? Value => value;
        public VersionTag? VersionTag { get; set; }
    }
}
