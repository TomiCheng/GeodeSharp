namespace Geode.Client;

/// <summary>
/// Thrown when a user-installed <c>CacheListener</c> callback
/// (<c>AfterCreateAsync</c> / <c>AfterUpdateAsync</c> /
/// <c>AfterDestroyAsync</c> / <c>AfterInvalidateAsync</c>, …) throws while
/// the region dispatches an after-event. Mirrors cppcache
/// <c>CacheListenerException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp</c>;
/// <c>GfErrType::GF_CACHE_LISTENER_EXCEPTION</c>) — the listener's own
/// exception is logged and wrapped here so callers can distinguish a
/// listener failure from a transport / server error.
/// </summary>
public class CacheListenerException : GeodeException
{
    public CacheListenerException() { }

    public CacheListenerException(string message)
        : base(message) { }

    public CacheListenerException(string message, Exception innerException)
        : base(message, innerException) { }
}
