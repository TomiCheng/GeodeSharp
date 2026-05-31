using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Internal;

partial class LocalRegion
{

    class DestroyActions(ILogger<DestroyActions> logger, LocalRegion region,
        object key, object? callbackArgument, int updateCount,
        CacheEventFlags eventFlags, VersionTag? versionTag = null, DataInput? delta = null)
        : IRegionAction
    {

        static readonly ObjectFactory<DestroyActions> _objectFactory
            = ActivatorUtilities.CreateFactory<DestroyActions>(
                [typeof(LocalRegion), typeof(object), typeof(object),
                 typeof(int), typeof(CacheEventFlags), typeof(VersionTag), typeof(DataInput)]);

        internal static DestroyActions Create(
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

        public void CheckArgs()
        {
            // cppcache DestroyActions::checkArgs (LocalRegion.cpp:1260-1267):
            //   key-only guard (GF_CACHE_ILLEGAL_ARGUMENT_EXCEPTION). Destroy
            //   ignores value + delta, unlike PutActions which also rejects
            //   value==null && delta==null.
            ArgumentNullException.ThrowIfNull(key);
        }
        public void GetCallbackOldValue()
        {
            // cppcache DestroyActions::getCallbackOldValue (LocalRegion.cpp:1269-1276).
            // Identical body to PutActions.GetCallbackOldValue — fetch only when
            // caching is enabled; out-params (entry, oldValue) land on
            // this.Entry / this.OldValue.
            if (region.Attributes.CachingEnabled)
            {
                (Entry, OldValue) = region.InternalEntriesMap.GetEntry(Key);
            }
        }
        public Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct)
        {
            // cppcache DestroyActions::localUpdate (LocalRegion.cpp:1293-1352).
            // Unlike PutActions (which forwards to LocalRegion::putLocal), destroy
            // inlines its local logic here — the only cross-type call is
            // EntriesMap.Remove.
            var cachingEnabled = region.Attributes.CachingEnabled;

            if (cachingEnabled)
            {
                object? oldValue;
                MapEntry? entry;
                try
                {
                    (entry, oldValue) = region.InternalEntriesMap.Remove(
                        key, updateCount, versionTag, remoteOpDone);
                }
                catch
                {
                    // cppcache: notifications swallow the not-found / version
                    // error so a delete event with no local entry isn't surfaced
                    // as an op failure; either way we skip the success-path
                    // stats below (cppcache returns early when remove fails).
                    if (eventFlags.IsNotification())
                    {
                        return Task.CompletedTask;
                    }
                    throw;
                }

                if (oldValue is not null)
                {
                    // any cleanup required for the entry (e.g. removing from LRU list)
                    entry?.Cleanup(eventFlags);

                    // entry/region expiration: don't refresh the access/modified
                    // timestamp when the destroy is itself driven by eviction
                    // or expiration.
                    if (!eventFlags.IsEvictOrExpire())
                    {
                        region.UpdateAccessAndModifiedTime(true);
                    }

                    // cppcache incEntries(-1) / setEntries(...) — folded into the
                    // pull-mode `Entries` ObservableGauge on CachePerfStatistics /
                    // RegionStatistics; no explicit call needed.
                }
            }

            // success-path stats (skipped when remove threw above)
            region._regionStats.Destroy();
            region._cachePerfStats.Destroy();
            return Task.CompletedTask;
        }
        public void LogCacheWriterFailure()
        {
            // cppcache DestroyActions::logCacheWriterFailure (LocalRegion.cpp:1278-1283):
            //   LOGFINER single destroy message (no create/update branch like Put).
            logger.LogTrace("Cache writer vetoed destroy for key {Key}", Key);
        }
        public async Task RemoteUpdateAsync(CancellationToken ct)
        {
            // cppcache DestroyActions::remoteUpdate (LocalRegion.cpp:1285-1291):
            //   return m_region.destroyNoThrow_remote(key, aCallbackArgument, versionTag);
            // No value (destroy); out-param versionTag -> this.VersionTag.
            VersionTag = await region
                .DestroyNoThrowRemoteAsync(Key, CallbackArgument, ct: ct)
                .ConfigureAwait(false);
        }

        public bool AddIfAbsent => true;
        public EntryEventType AfterEventType => EntryEventType.AfterDestroy;
        public EntryEventType BeforeEventType => EntryEventType.BeforeDestroy;
        public object? CallbackArgument => callbackArgument;
        public MapEntry? Entry { get; set; }
        public CacheEventFlags EventFlags => eventFlags;
        public bool FailIfPresent => false;
        public object Key => key;
        public string Name => "Region::destroy";
        public object? OldValue { get; set; }
        public TXState? TxState { get; } = region.GetTXState();
        public int UpdateCount { get; set; } = updateCount;
        public VersionTag? VersionTag { get; set; }

        // cppcache destroyNoThrow calls updateNoThrow<DestroyActions>(key,
        //   nullptr, ...) — destroy carries no value.
        public object? Value => null;
    }
}
