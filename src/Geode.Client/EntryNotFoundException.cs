namespace Geode.Client;

/// <summary>
/// Thrown by strict-delete ops (<see cref="IRegion.DestroyAsync"/>) when
/// no entry with the requested key is present in the region. Mirrors
/// cppcache <c>EntryNotFoundException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp</c>).
/// Non-strict siblings (<see cref="IRegion.RemoveExAsync"/>) return
/// <see langword="false"/> instead.
/// </summary>
public class EntryNotFoundException : GeodeException
{
    public EntryNotFoundException() { }

    public EntryNotFoundException(string message)
        : base(message) { }

    public EntryNotFoundException(string message, Exception innerException)
        : base(message, innerException) { }
}
