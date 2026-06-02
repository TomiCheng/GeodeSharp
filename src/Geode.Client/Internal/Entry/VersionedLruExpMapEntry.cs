namespace Geode.Client.Internal.Entry;

internal class VersionedLruExpMapEntry(ExpiryTaskManager expiryTaskManager, object key)
    : MapEntry(key), ILruEntryProperties, IVersionStamp, IExpEntryProperties
{
    private readonly LruEntryProperties _lruEntryProperties = new();
    private readonly VersionStamp _versionStamp = new();
    private readonly ExpEntryProperties _expEntryProperties = new();

    public LruEntryProperties LruProperties => _lruEntryProperties;
    public VersionStamp Stamp => _versionStamp;
    public ExpEntryProperties ExpProperties => _expEntryProperties;
}
