namespace Geode.Client;

/// <summary>
/// Thrown when a user-installed <c>CacheWriter</c> vetoes a CRUD op
/// (returns <see langword="false"/> from its <c>BeforeUpdate</c> /
/// <c>BeforeCreate</c> / <c>BeforeDestroy</c> / <c>BeforeInvalidate</c>
/// callback), or when the writer callback itself throws. Mirrors
/// cppcache <c>CacheWriterException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:111</c>).
/// </summary>
public class CacheWriterException : GeodeException
{
    public CacheWriterException() { }

    public CacheWriterException(string message)
        : base(message) { }

    public CacheWriterException(string message, Exception innerException)
        : base(message, innerException) { }
}
