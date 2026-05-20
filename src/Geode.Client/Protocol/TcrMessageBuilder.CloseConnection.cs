using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.CloseConnection"/> request frame.
    /// Mirrors cppcache <c>TcrMessageCloseConnection</c>
    /// (<c>cppcache/src/TcrMessage.cpp:2051-2060</c>).
    /// </summary>
    /// <param name="keepAlive">
    /// Whether the server should preserve this client's subscription
    /// queue (Phase 2+ HA / durable client). Phase 1.1 always passes
    /// <c>false</c> ??no subscription state worth keeping.
    /// </param>
    /// <remarks>
    /// One <see cref="TcrPart"/>: <c>IsObject=0</c>, payload = 1 byte
    /// (the <paramref name="keepAlive"/> bool). cppcache writes this as
    /// <c>writeBoolean(keepAlive)</c> after a <c>writeBoolean(false)</c>
    /// for <c>IsObject</c>, but the IsObject byte lives in our Part
    /// header ??only the payload byte goes inside the Part.
    /// </remarks>
    public TcrMessage CloseConnection(bool keepAlive) =>
        new(
            MessageType: MessageType.CloseConnection,
            TransactionId: MetaTransactionId,
            EarlyAck: 0,
            Parts: [partBuilder.RawBytes(new byte[] { keepAlive ? (byte)1 : (byte)0 })],
            ServiceProvider: _serviceProvider);
}
