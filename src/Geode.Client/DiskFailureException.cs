namespace Geode.Client;

/// <summary>
/// Thrown by an <see cref="IPersistenceManager"/> when a write fails due to
/// disk failure. Mirrors cppcache <c>DiskFailureException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:466</c>).
/// </summary>
public class DiskFailureException : GeodeException
{
    public DiskFailureException() { }

    public DiskFailureException(string message)
        : base(message) { }

    public DiskFailureException(string message, Exception innerException)
        : base(message, innerException) { }
}
