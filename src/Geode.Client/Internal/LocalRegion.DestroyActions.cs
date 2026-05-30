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
            throw new NotImplementedException();
            // cppcache DestroyActions::getCallbackOldValue (LocalRegion.cpp:1269-1276):
            //
            //   inline void getCallbackOldValue(bool cachingEnabled,
            //                                   const std::shared_ptr<CacheableKey>& key,
            //                                   std::shared_ptr<MapEntryImpl>& entry,
            //                                   std::shared_ptr<Cacheable>& oldValue) const {
            //     if (cachingEnabled) {
            //       m_region.m_entries->getEntry(key, entry, oldValue);
            //     }
            //   }
            //
            // Identical to PutActions.GetCallbackOldValue — `entry` + `oldValue`
            //   are out-params → land on this.Entry / this.OldValue:
            //   if (cachingEnabled) (Entry, OldValue) =
            //       localRegion.LocalEntriesMap.Value!.GetEntry(Key);
        }
        public async Task LocalUpdateAsync(int updateCount, bool remoteOpDone, CancellationToken ct)
        {
            throw new NotImplementedException();
            // cppcache DestroyActions::localUpdate (LocalRegion.cpp:1293-1352):
            //
            //   inline GfErrType localUpdate(const std::shared_ptr<CacheableKey>& key,
            //                                const std::shared_ptr<Cacheable>& /*value*/,
            //                                std::shared_ptr<Cacheable>& oldValue,
            //                                bool cachingEnabled,
            //                                const CacheEventFlags eventFlags,
            //                                int updateCount,
            //                                std::shared_ptr<VersionTag> versionTag,
            //                                DataInput* /*delta*/ = nullptr,
            //                                std::shared_ptr<EventId> /*eventId*/ = nullptr,
            //                                bool afterRemote = false) {
            //     auto& cachePerfStats = m_region.m_cacheImpl->getCachePerfStats();
            //
            var cachingEnabled = region.Attributes.CachingEnabled;
            
            if (cachingEnabled) {
            //       std::shared_ptr<MapEntryImpl> entry;
            //       //  for notification invoke the listener even if the key does
            //       // not exist locally
            //       GfErrType err;
            //       LOGDEBUG("Region::destroy: region [%s] destroying key [%s]",
            //                m_region.getFullPath().c_str(),
            //                Utils::nullSafeToString(key).c_str());
            //       if ((err = m_region.m_entries->remove(key, oldValue, entry, updateCount,
            //                                             versionTag, afterRemote)) !=
            //           GF_NOERR) {
            //         if (eventFlags.isNotification()) {
            //           LOGDEBUG(
            //               "Region::destroy: region [%s] destroy key [%s] for "
            //               "notification having value [%s] failed with %d",
            //               m_region.getFullPath().c_str(),
            //               Utils::nullSafeToString(key).c_str(),
            //               Utils::nullSafeToString(oldValue).c_str(), err);
            //           err = GF_NOERR;
            //         }
            //         return err;
            //       }
            //
            //       if (oldValue != nullptr) {
            //         LOGDEBUG(
            //             "Region::destroy: region [%s] destroyed key [%s] having "
            //             "value [%s]",
            //             m_region.getFullPath().c_str(),
            //             Utils::nullSafeToString(key).c_str(),
            //             Utils::nullSafeToString(oldValue).c_str());
            //         // any cleanup required for the entry (e.g. removing from LRU list)
            //         if (entry != nullptr) {
            //           entry->cleanup(eventFlags);
            //         }
            //         // entry/region expiration
            //         if (!eventFlags.isEvictOrExpire()) {
            //           m_region.updateAccessAndModifiedTime(true);
            //         }
            //         // update the stats
            //         m_region.m_regionStats->setEntries(m_region.m_entries->size());
            //         cachePerfStats.incEntries(-1);
            //       }
            }
            //     // update the stats
            //     m_region.m_regionStats->incDestroys();
            //     cachePerfStats.incDestroys();
            //     return GF_NOERR;
            //}
            //
            // Unlike PutActions (which forwards to LocalRegion::putLocal), destroy
            //   inlines its local logic here — the only cross-type call is
            //   m_entries->remove. err-codes ride GfErrTypeException for pipeline
            //   signals; the notification branch swallows the not-found err
            //   (err = GF_NOERR). cachePerfStats/regionStats -> CachePerfStats /
            //   RegionStats. `oldValue`/`entry` out-params -> this.OldValue / this.Entry.
            // Delegate -> m_entries->remove == ConcurrentEntriesMap::remove
            //   (ConcurrentEntriesMap.cpp:134-148) => C# EntriesMap.Remove (mirror of
            //   how PutActions.LocalUpdateAsync uses LocalEntriesMap.Value!.Put).
            //   Run /cpp-stub on ConcurrentEntriesMap::remove separately if its C#
            //   port isn't ready.
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
