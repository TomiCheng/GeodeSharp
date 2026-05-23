using System.Collections.ObjectModel;
using System.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.Ping"/> request frame.
    /// Mirrors cppcache <c>TcrMessagePing</c>.
    /// </summary>
    private ValueTask<TcrMessage> BuildPingAsync(CancellationToken ct)
    {
        return ValueTask.FromResult(ActivatorUtilities.CreateInstance<TcrMessage>(
            _serviceProvider, _messageType, _transactionId, _earlyAck, ReadOnlyCollection<TcrPart>.Empty));
    }
}
