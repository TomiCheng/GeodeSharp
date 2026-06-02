namespace Geode.Client.Internal.Entry;

internal class VersionedExpMapEntry(ExpiryTaskManager expiryTaskManager, object key)
    : MapEntry(key), IExpEntryProperties, IVersionStamp
{
    private readonly ExpEntryProperties _expEntryProperties = new();
    private readonly VersionStamp _versionStamp = new();

    public ExpEntryProperties ExpProperties => _expEntryProperties;
    public VersionStamp Stamp => _versionStamp;
}
