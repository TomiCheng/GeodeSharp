/*
namespace Geode.Client;

/// <summary>
/// Base exception for Geode-specific protocol-level failures: server-side
/// refusals (e.g. handshake rejection), malformed wire bytes, and exceptions
/// returned by the server in <c>MessageType.Exception</c> replies.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="System.IO.IOException"/> /
/// <see cref="System.Net.Sockets.SocketException"/> which surface for
/// genuine transport failures, and from
/// <see cref="System.InvalidOperationException"/> which is reserved for API
/// misuse (e.g. <c>SendAsync</c> before <c>ConnectAsync</c>).
/// </para>
/// <para>
/// Catch this type to handle "the Geode server said something we couldn't
/// proceed with" without swallowing unrelated BCL failures.
/// </para>
/// </remarks>
public class GeodeException : Exception
{
    public GeodeException() { }

    public GeodeException(string message)
        : base(message) { }

    public GeodeException(string message, Exception innerException)
        : base(message, innerException) { }
}

*/