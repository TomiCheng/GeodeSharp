namespace Geode.Client;

/// <summary>
/// Thrown by strict-insert ops (<c>Create</c>) when an entry with the
/// requested key is already present in the region. Mirrors cppcache
/// <c>EntryExistsException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:355</c>).
/// </summary>
public class EntryExistsException : GeodeException
{
    public EntryExistsException() { }

    public EntryExistsException(string message)
        : base(message) { }

    public EntryExistsException(string message, Exception innerException)
        : base(message, innerException) { }
}
