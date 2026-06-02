namespace Geode.Client.Internal.Entry;

internal class VersionedLruMapEntry(object key)
    : MapEntry(key), ILruEntryProperties, IVersionStamp
{
    private readonly LruEntryProperties _lruEntryProperties = new();
    private readonly VersionStamp _versionStamp = new();

    public LruEntryProperties LruProperties => _lruEntryProperties;
    public VersionStamp Stamp => _versionStamp;
}
