namespace Geode.Client;

/// <summary>
/// Thrown by a value's <c>IDelta.FromDelta</c> implementation when the
/// incoming delta can't be applied to the existing value. Caught
/// internally by the entry-map writeback path and converted into a
/// pipeline <c>InvalidDelta</c> signal that triggers a full-object
/// re-fetch. Mirrors cppcache <c>InvalidDeltaException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:718</c>).
/// </summary>
public class InvalidDeltaException : GeodeException
{
    public InvalidDeltaException() { }

    public InvalidDeltaException(string message)
        : base(message) { }

    public InvalidDeltaException(string message, Exception innerException)
        : base(message, innerException) { }
}
