/*
namespace Geode.Client;

/// <summary>
/// Thrown when the Geode server requires authentication but the client
/// connected without credentials. Mirrors cppcache
/// <c>AuthenticationRequiredException</c>; corresponds to
/// <c>GfErrType::GF_AUTHENTICATION_REQUIRED_EXCEPTION</c>.
/// </summary>
/// <remarks>
/// Treated as a fatal-client failure by the pool's failover retry loop:
/// the missing credentials apply to every server in the cluster, so
/// retrying on a different host doesn't help. Phase 3 security wires
/// this in; MVP never throws it.
/// </remarks>
public class AuthenticationRequiredException : GeodeException
{
    public AuthenticationRequiredException() { }

    public AuthenticationRequiredException(string message)
        : base(message) { }

    public AuthenticationRequiredException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/