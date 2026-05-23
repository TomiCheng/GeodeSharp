/*
namespace Geode.Client;

/// <summary>
/// Thrown when the client cannot reach any configured locator. Mirrors
/// cppcache <c>NoAvailableLocatorsException</c>; corresponds to
/// <c>GfErrType::GF_CACHE_LOCATOR_EXCEPTION</c>.
/// </summary>
/// <remarks>
/// Treated as a fatal-client failure by the pool's failover retry loop:
/// every server reachability path ultimately depends on a working locator,
/// so retrying on a different host won't help — propagate to the caller.
/// </remarks>
public class NoAvailableLocatorsException : GeodeException
{
    public NoAvailableLocatorsException() { }

    public NoAvailableLocatorsException(string message)
        : base(message) { }

    public NoAvailableLocatorsException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/