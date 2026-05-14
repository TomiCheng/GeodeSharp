namespace Geode.Client;

/// <summary>
/// Factory for OQL queries. Mirrors cppcache <c>QueryService</c>
/// (<c>cppcache/include/geode/QueryService.hpp</c>), reduced to the
/// Phase 1.4 surface &#x2014; Continuous Query (<c>newCq</c> /
/// <c>closeCqs</c> / ...) is Phase 2 scope and not surfaced here.
/// </summary>
/// <remarks>
/// Obtained from <see cref="IRegionService.QueryService"/>. The
/// returned <see cref="IQuery{T}"/> is not sent to the server until
/// its <c>ExecuteAsync</c> is called.
/// </remarks>
public interface IQueryService
{
    /// <summary>
    /// Build an <see cref="IQuery{T}"/> for <paramref name="oql"/>. The
    /// query is not parsed locally and not sent to the server until
    /// <c>ExecuteAsync</c> is called on the returned query. Mirrors
    /// cppcache <c>QueryService::newQuery(querystr)</c>.
    /// </summary>
    /// <typeparam name="T">
    /// Expected row type. For <c>SELECT *</c> the value type of the
    /// region; for <c>SELECT COUNT(*)</c> use <see cref="int"/> and
    /// take the single element. Phase 2 multi-column projection will
    /// introduce a <c>Struct</c> row type.
    /// </typeparam>
    /// <param name="oql">The OQL string.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="oql"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    IQuery<T> NewQuery<T>(string oql);
}
