namespace Geode.Client.Internal.Entry;

internal class LruExpMapEntry(ExpiryTaskManager expiryTaskManager, object key)
    : MapEntry(key), ILruEntryProperties, IExpEntryProperties
{
    private readonly LruEntryProperties _lruEntryProperties = new();
    private readonly ExpEntryProperties _expEntryProperties = new();

    public LruEntryProperties LruProperties => _lruEntryProperties;
    public ExpEntryProperties ExpProperties => _expEntryProperties;
}
