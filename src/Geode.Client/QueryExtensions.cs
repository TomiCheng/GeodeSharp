namespace Geode.Client;

/// <summary>
/// Convenience extensions over <see cref="IQuery{T}"/>.
/// </summary>
public static class QueryExtensions
{
    /// <summary>
    /// Execute and return the single row. Throws when the result has
    /// zero or more than one row.
    /// </summary>
    public static async Task<T> ExecuteSingleAsync<T>(
        this IQuery<T> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await query.ExecuteAsync(ct).ConfigureAwait(false);
        return rows.Single();
    }

    /// <summary>
    /// Execute and return the first row, or <c>default</c> when the
    /// result is empty.
    /// </summary>
    public static async Task<T?> ExecuteFirstOrDefaultAsync<T>(
        this IQuery<T> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await query.ExecuteAsync(ct).ConfigureAwait(false);
        return rows.Count == 0 ? default : rows[0];
    }

    /// <summary>
    /// Replace <see cref="IQuery{T}.Parameters"/> with
    /// <paramref name="parameters"/>. Returns <paramref name="query"/>
    /// for fluent chaining.
    /// </summary>
    public static IQuery<T> WithParameters<T>(
        this IQuery<T> query, params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parameters);

        query.Parameters.Clear();
        foreach (var p in parameters)
        {
            query.Parameters.Add(p);
        }
        return query;
    }

    /// <summary>
    /// Set <see cref="IQuery{T}.ResponseTimeout"/>. Returns
    /// <paramref name="query"/> for fluent chaining.
    /// </summary>
    public static IQuery<T> WithResponseTimeout<T>(
        this IQuery<T> query, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.ResponseTimeout = timeout;
        return query;
    }
}
