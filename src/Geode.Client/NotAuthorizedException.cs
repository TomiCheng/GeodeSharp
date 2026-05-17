namespace Geode.Client;

/// <summary>
/// Thrown when the client authenticated successfully but lacks the
/// permission required for the attempted operation. Mirrors cppcache
/// <c>NotAuthorizedException</c>; corresponds to
/// <c>GfErrType::GF_NOT_AUTHORIZED_EXCEPTION</c>.
/// </summary>
/// <remarks>
/// Treated as a fatal-client failure by the pool's failover retry loop:
/// permissions are server-cluster-wide, so retrying on a different host
/// will fail the same way. Phase 3 security wires this in; MVP never
/// throws it.
/// </remarks>
public class NotAuthorizedException : GeodeException
{
    public NotAuthorizedException() { }

    public NotAuthorizedException(string message)
        : base(message) { }

    public NotAuthorizedException(string message, Exception innerException)
        : base(message, innerException) { }
}
