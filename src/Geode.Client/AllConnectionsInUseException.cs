/*
namespace Geode.Client;

/// <summary>
/// Thrown when every connection in the pool is currently in use and
/// <see cref="Options.CachePoolOptions.MaxConnections"/> forbids
/// opening another one. Mirrors cppcache
/// <c>AllConnectionsInUseException</c>; corresponds to
/// <c>GfErrType::GF_ALL_CONNECTIONS_IN_USE</c> (cppcache pool's
/// <c>maxConnLimit</c> flag).
/// </summary>
/// <remarks>
/// Phase 1.5 待辦: surfaces once <c>CreatePoolConnectionAsync</c>
/// enforces the <c>MaxConnections</c> cap. Treated as a transient
/// failure — the caller can wait on
/// <see cref="Options.CachePoolOptions.FreeConnectionTimeout"/> for a
/// conn to return, or fail the op when the wait budget expires.
/// </remarks>
public class AllConnectionsInUseException : GeodeException
{
    public AllConnectionsInUseException() { }

    public AllConnectionsInUseException(string message)
        : base(message) { }

    public AllConnectionsInUseException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/