namespace Geode.Client;

/// <summary>
/// A reusable handle for an OQL query. Build with
/// <see cref="IQueryService.NewQuery{T}"/>; call
/// <see cref="ExecuteAsync(CancellationToken)"/> (or the parameterised
/// overload) to send the query to the server. Mirrors cppcache
/// <c>Query</c> (<c>cppcache/include/geode/Query.hpp</c>), reduced to
/// the Phase 1.4 surface &#x2014; client-side <c>compile()</c> /
/// <c>isCompiled()</c> were never supported upstream and are omitted.
/// </summary>
/// <typeparam name="T">
/// Row type the result is decoded as. For <c>SELECT *</c> the value
/// type of the region; for <c>SELECT COUNT(*)</c> use <see cref="int"/>
/// and take the single element. Phase 2 multi-column projection will
/// introduce a <c>Struct</c> row type.
/// </typeparam>
/// <remarks>
/// Not thread-safe per cppcache contract &#x2014; concurrent
/// <c>ExecuteAsync</c> calls on the same instance are undefined; use
/// one <see cref="IQuery{T}"/> per thread / scope.
/// </remarks>
public interface IQuery<T>
{
    /// <summary>
    /// The OQL string this query was created with. Mirrors cppcache
    /// <c>Query::getQueryString()</c>.
    /// </summary>
    string QueryString { get; }

    /// <summary>
    /// Server-side response timeout. After this duration the server
    /// aborts the query and returns an error reply (it does not affect
    /// how long this client waits &#x2014; use the
    /// <see cref="CancellationToken"/> for that). Default is 15
    /// seconds, matching cppcache
    /// <c>DEFAULT_QUERY_RESPONSE_TIMEOUT</c>
    /// (<c>cppcache/include/geode/internal/geode_base.hpp</c>).
    /// </summary>
    TimeSpan ResponseTimeout { get; set; }

    /// <summary>
    /// Execute the OQL on the server and return all rows. Mirrors
    /// cppcache <c>Query::execute()</c> &#x2192; wire
    /// <c>MessageType.Query(34)</c>. For <c>SELECT COUNT(*)</c> the
    /// list has a single element &#x2014; use
    /// <see cref="System.Linq.Enumerable.Single{TSource}(IEnumerable{TSource})"/>
    /// to extract.
    /// </summary>
    /// <exception cref="GeodeException">
    /// Server returned a query / parse error, or the server is not
    /// reachable.
    /// </exception>
    Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default);

    /// <summary>
    /// Execute a parameterised OQL on the server. The OQL uses
    /// positional placeholders <c>$1</c>, <c>$2</c>, ...;
    /// <paramref name="parameters"/> supplies the values in order.
    /// Mirrors cppcache <c>Query::execute(paramList)</c> &#x2192; wire
    /// <c>MessageType.QueryWithParameters(82)</c>.
    /// </summary>
    /// <param name="parameters">
    /// Positional bind values; each element is serialised through the
    /// usual built-in type codecs. <see langword="null"/> entries are
    /// sent as OQL <c>NULL</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parameters"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="GeodeException">
    /// Server returned a query / parse error, or the server is not
    /// reachable.
    /// </exception>
    Task<IReadOnlyList<T>> ExecuteAsync(
        IReadOnlyList<object?> parameters,
        CancellationToken ct = default);
}
