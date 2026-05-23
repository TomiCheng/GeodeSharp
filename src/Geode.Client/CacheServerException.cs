namespace Geode.Client;

/// <summary>
/// Thrown when the Geode server returns a <c>MessageType.Exception</c>
/// reply to a client request (Put / Get / Query / etc.). Mirrors
/// cppcache <c>CacheServerException</c>; corresponds to
/// <c>GfErrType::GF_CACHESERVER_EXCEPTION</c> (server-side failure
/// surfaced in the reply rather than a transport issue).
/// </summary>
/// <remarks>
/// The message body carries the server-supplied Java exception class
/// name + stack trace decoded from the wire payload.
/// </remarks>
public class CacheServerException : GeodeException
{
    public CacheServerException() { }

    public CacheServerException(string message)
        : base(message) { }

    public CacheServerException(string message, Exception innerException)
        : base(message, innerException) { }
}
