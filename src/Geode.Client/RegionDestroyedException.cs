namespace Geode.Client;

/// <summary>
/// Thrown by region ops that race against region disposal — once
/// <c>DestroyRegion</c> flips the lifecycle flag, every subsequent
/// CRUD call surfaces this exception. Mirrors cppcache
/// <c>RegionDestroyedException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:171</c>).
/// </summary>
public class RegionDestroyedException : GeodeException
{
    public RegionDestroyedException() { }

    public RegionDestroyedException(string message)
        : base(message) { }

    public RegionDestroyedException(string message, Exception innerException)
        : base(message, innerException) { }
}
