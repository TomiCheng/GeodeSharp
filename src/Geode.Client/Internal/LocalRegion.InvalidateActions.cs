using Geode.Client.Internal.Entry;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

partial class LocalRegion
{
    class InvalidateActions(ILogger<InvalidateActions> logger, LocalRegion region,
        object key, object? callbackArgument, int updateCount,
        CacheEventFlags eventFlags, VersionTag? versionTag = null, DataInput? delta = null)
        : IRegionAction
    {
        static readonly ObjectFactory<InvalidateActions> _objectFactory
            = ActivatorUtilities.CreateFactory<InvalidateActions>(
                [typeof(LocalRegion), typeof(object), typeof(object),
                 typeof(int), typeof(CacheEventFlags), typeof(VersionTag), typeof(DataInput)]);

        internal static InvalidateActions Create(
            IServiceProvider serviceProvider,
            LocalRegion region,
            object key,
            object? callbackArgument,
            int updateCount,
            CacheEventFlags eventFlags,
            VersionTag? versionTag = null,
            DataInput? delta = null)
            => _objectFactory(serviceProvider,
                [region, key, callbackArgument, updateCount, eventFlags, versionTag, delta]);

        public void CheckArgs() => ArgumentNullException.ThrowIfNull(key);

        public void GetCallbackOldValue()
        {
            if (region.Attributes.CachingEnabled)
            {
                (Entry, OldValue) = region.InternalEntriesMap.GetEntry(Key);
            }
        }

        public Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct)
            => region.InvalidateLocalAsync(Name, Key, Value, eventFlags, versionTag, ct);

        public void LogCacheWriterFailure()
            => logger.LogTrace("Cache writer vetoed invalidate for key {Key}", Key);

        public async Task RemoteUpdateAsync(CancellationToken ct)
            => VersionTag = await region
                .InvalidateNoThrowRemoteAsync(Key, CallbackArgument, ct: ct)
                .ConfigureAwait(false);

        public bool AddIfAbsent => true;
        public EntryEventType AfterEventType => EntryEventType.AfterInvalidate;
        public EntryEventType BeforeEventType => EntryEventType.BeforeInvalidate;
        public object? CallbackArgument => callbackArgument;
        public MapEntry? Entry { get; set; }
        public CacheEventFlags EventFlags => eventFlags;
        public bool FailIfPresent => false;
        public object Key => key;
        public string Name => "Region::invalidate";
        public object? OldValue { get; set; }
        public TXState? TxState { get; } = region.GetTXState();
        public int UpdateCount { get; set; } = updateCount;
        public VersionTag? VersionTag { get; set; }
        public object? Value => null;
    }
}
