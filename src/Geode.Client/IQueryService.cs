namespace Geode.Client;

/// <summary>
/// Factory for OQL queries. Obtained from
/// <see cref="IGeodeCache.GetQueryService(string?)"/>.
/// </summary>
public interface IQueryService
{
    /// <summary>
    /// Build an <see cref="IQuery{T}"/> for <paramref name="oql"/>.
    /// Does not send anything to the server until
    /// <see cref="IQuery{T}.ExecuteAsync(CancellationToken)"/> is
    /// called.
    /// </summary>
    /// <typeparam name="T">Expected row type.</typeparam>
    /// <param name="oql">The OQL string.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="oql"/> is <see langword="null"/>, empty, or whitespace.
    /// </exception>
    IQuery<T> NewQuery<T>(string oql);
}
