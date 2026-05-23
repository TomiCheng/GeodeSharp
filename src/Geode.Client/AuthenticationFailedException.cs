/*
namespace Geode.Client;

/// <summary>
/// Thrown when the Geode server rejects the client's credentials during
/// handshake or a privileged op. Mirrors cppcache
/// <c>AuthenticationFailedException</c>; corresponds to
/// <c>GfErrType::GF_AUTHENTICATION_FAILED_EXCEPTION</c>.
/// </summary>
/// <remarks>
/// Treated as a fatal-client failure by the pool's failover retry loop:
/// every server in the cluster shares the same auth realm, so retrying
/// on a different host doesn't help. Phase 3 security wires this in;
/// MVP never throws it.
/// </remarks>
public class AuthenticationFailedException : GeodeException
{
    public AuthenticationFailedException() { }

    public AuthenticationFailedException(string message)
        : base(message) { }

    public AuthenticationFailedException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/