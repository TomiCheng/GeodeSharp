/*
namespace Geode.Client;

/// <summary>
/// A reusable handle for an OQL query.
/// </summary>
/// <typeparam name="T">Row type the result is decoded as.</typeparam>
public interface IQuery<T>
{
    /// <summary>The OQL string this query was created with.</summary>
    string QueryString { get; }

    /// <summary>
    /// Server-side response timeout.
    /// </summary>
    TimeSpan ResponseTimeout { get; set; }

    /// <summary>
    /// Positional bind values for OQL placeholders <c>$1</c>,
    /// <c>$2</c>, ...
    /// </summary>
    IList<object?> Parameters { get; }

    /// <summary>Execute the OQL on the server and return all rows.</summary>
    /// <exception cref="GeodeException">Server-side query / parse error.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The owning query service has been closed.
    /// </exception>
    Task<IReadOnlyList<T>> ExecuteAsync(CancellationToken ct = default);
}

*/