namespace Geode.Client;

/// <summary>
/// Thrown on a fatal internal inconsistency that should never happen in a
/// correct client (e.g. an overflow eviction finding a destroyed entry in
/// the LRU queue). Mirrors cppcache <c>FatalInternalException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:453</c>).
/// </summary>
public class FatalInternalException : GeodeException
{
    public FatalInternalException() { }

    public FatalInternalException(string message)
        : base(message) { }

    public FatalInternalException(string message, Exception innerException)
        : base(message, innerException) { }
}
