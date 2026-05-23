namespace Geode.Client;

/// <summary>
/// Thrown by <see cref="IRegionFactory.CreateAsync{TKey, TValue}(string, CancellationToken)"/>
/// when a region with the requested name is already registered on the
/// cache. Mirrors cppcache <c>RegionExistsException</c>
/// (<c>cppcache/include/geode/ExceptionTypes.hpp:123</c>).
/// </summary>
public class RegionExistsException : GeodeException
{
    public RegionExistsException() { }

    public RegionExistsException(string message)
        : base(message) { }

    public RegionExistsException(string message, Exception innerException)
        : base(message, innerException) { }
}
