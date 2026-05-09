namespace Geode.Client.Protocol.Operations;

/// <summary>
/// <see cref="MessageType.Ping"/> operation on top of
/// <see cref="TcrConnection.SendRequestAsync"/>.
/// </summary>
/// <remarks>
/// Lives as an extension method (not a method on
/// <see cref="TcrConnection"/>) so the connection class stays focused on
/// transport. When the connection pool lands in Phase 6 the wrapper may
/// move to a pool-aware location; the public call site
/// <c>connection.PingAsync(ct)</c> can stay the same shape.
/// </remarks>
internal static class PingExtensions
{
    /// <summary>
    /// Send a <see cref="MessageType.Ping"/> (5) and wait for the server's
    /// <see cref="MessageType.Reply"/> (6). Mirrors cppcache
    /// <c>TcrMessagePing</c>.
    /// </summary>
    /// <exception cref="GeodeException">
    /// Server returned a <see cref="TcrMessage.MessageType"/> other than
    /// <see cref="MessageType.Reply"/> (e.g. an Exception reply carrying
    /// error text in its parts).
    /// </exception>
    /// <remarks>
    /// Ping is a "meta" request with no transaction context, so we send
    /// <c>TransactionId = -1</c> to match cppcache's <c>writeHeader</c>
    /// behaviour when no <c>TxState</c> is present. We do not validate
    /// the reply's <c>TransactionId</c> echo — a single connection only
    /// has one in-flight request at a time, and the server's echo
    /// semantics for meta ops are unspecified.
    /// </remarks>
    public static async Task PingAsync(
        this TcrConnection connection,
        CancellationToken cancellationToken = default)
    {
        // cppcache MetaTransactionId — used for any request that isn't
        // part of a Geode transaction.
        const int MetaTransactionId = -1;

        var ping = new TcrMessage(
            MessageType: MessageType.Ping,
            TransactionId: MetaTransactionId,
            EarlyAck: 0,
            Parts: []);

        var reply = await connection.SendRequestAsync(ping, cancellationToken).ConfigureAwait(false);
        if (reply.MessageType != MessageType.Reply)
        {
            throw new GeodeException(
                $"Expected Reply ({(int)MessageType.Reply}) to Ping, got " +
                $"{reply.MessageType} ({(int)reply.MessageType}).");
        }
    }
}
