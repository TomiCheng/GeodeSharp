namespace Geode.Client;

/// <summary>
/// Thrown when a user-installed <c>CacheLoader</c> fails — its
/// <c>LoadAsync</c> callback threw while resolving a value on a
/// <see cref="IRegion.GetAsync"/> miss. Mirrors cppcache
/// <c>CacheLoaderException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp</c>;
/// <c>GfErrType::GF_CACHE_LOADER_EXCEPTION</c>) — the loader's own
/// exception is logged and wrapped here so callers can distinguish a
/// loader failure from a transport / server error.
/// </summary>
public class CacheLoaderException : GeodeException
{
    public CacheLoaderException() { }

    public CacheLoaderException(string message)
        : base(message) { }

    public CacheLoaderException(string message, Exception innerException)
        : base(message, innerException) { }
}
