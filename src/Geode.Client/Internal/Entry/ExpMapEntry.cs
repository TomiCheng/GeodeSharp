namespace Geode.Client.Internal.Entry;

internal class ExpMapEntry(ExpiryTaskManager expiryTaskManager, object key)
    : MapEntry(key), IExpEntryProperties
{
    private readonly ExpEntryProperties _expEntryProperties = new();

    public ExpEntryProperties ExpProperties => _expEntryProperties;
}
