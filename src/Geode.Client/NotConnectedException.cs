/*
namespace Geode.Client;

/// <summary>
/// Thrown when the client cannot reach a configured Geode server —
/// either the endpoint is marked disconnected, all connections in the
/// pool are dead, or no usable endpoint could be selected. Mirrors
/// cppcache <c>NotConnectedException</c>; corresponds to
/// <c>GfErrType::GF_NOTCON</c>.
/// </summary>
/// <remarks>
/// Treated as a transient failure by the pool's failover retry loop —
/// excluding the dead endpoint and trying the next server is the
/// expected recovery path. Propagates to the caller only after every
/// configured server has been tried and excluded.
/// </remarks>
public class NotConnectedException : GeodeException
{
    public NotConnectedException() { }

    public NotConnectedException(string message)
        : base(message) { }

    public NotConnectedException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/