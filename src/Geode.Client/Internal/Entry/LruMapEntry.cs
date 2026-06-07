namespace Geode.Client.Internal.Entry;

internal class LruMapEntry(object key)
    : MapEntry(key), ILruEntryProperties
{
    private readonly LruEntryProperties _lruEntryProperties = new();

    public LruEntryProperties LruProperties => _lruEntryProperties;

    public override void Cleanup(CacheEventFlags eventFlags)
    {
        // cppcache LRUMapEntry::cleanup (LRUMapEntry.hpp:73-79): on a
        // non-eviction removal, unlink this entry from the LRU list. cppcache
        // itself leaves the list-removal as a TODO. Here the LRU queue is
        // external (LRUEntriesMap owns the LRUQueue, keyed by entry), so removal
        // is done at the map level (LRUEntriesMap.RemoveAsync → LRUQueue.Remove);
        // the entry holds no queue reference, so there is nothing to do here.
        if (!eventFlags.HasFlag(CacheEventFlags.Eviction))
        {
            // no-op: LRU-queue unlink is handled by LRUEntriesMap, not the entry.
        }
    }
}
