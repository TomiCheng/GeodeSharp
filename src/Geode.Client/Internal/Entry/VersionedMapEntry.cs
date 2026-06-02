namespace Geode.Client.Internal.Entry;

internal class VersionedMapEntry(object key) : MapEntry(key), IVersionStamp
{
    private readonly VersionStamp _versionStamp = new();

    public VersionStamp Stamp => _versionStamp;
}
