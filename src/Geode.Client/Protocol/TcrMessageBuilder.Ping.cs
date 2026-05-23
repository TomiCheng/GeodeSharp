/*
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Ping"/> request frame.
    /// Mirrors cppcache <c>TcrMessagePing</c>.
    /// </summary>
    public TcrMessage Ping() =>
        new(
            MessageType: MessageType.Ping,
            TransactionId: MetaTransactionId,
            EarlyAck: 0,
            Parts: [], ServiceProvider: _serviceProvider);
}

*/