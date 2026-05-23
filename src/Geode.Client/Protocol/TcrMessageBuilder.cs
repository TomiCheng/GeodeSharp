using System.Collections.ObjectModel;
using Geode.Client.Internal;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

/// <summary>
/// Factory for the TCR request frames (<see cref="TcrMessage"/>) each
/// Geode operation puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One method per <see cref="MessageType"/>, each in its own partial
/// file (<c>TcrMessageBuilder.Ping.cs</c>, <c>TcrMessageBuilder.Put.cs</c>,
/// ...). The collection mirrors cppcache's <c>TcrMessage.hpp</c> family
/// of <c>TcrMessage*</c> subclasses (<c>TcrMessagePing</c>,
/// <c>TcrMessagePut</c>, <c>TcrMessageRequest</c>, ...) ??same
/// per-operation recipe, expressed as functions returning an immutable
/// <see cref="TcrMessage"/> rather than as a class hierarchy.
/// </para>
/// <para>
/// Pure functions: no I/O, no hidden state. The "send it + handle the
/// reply" half lives on <see cref="TcrConnection"/> as op methods
/// (<see cref="TcrConnection.PingAsync"/> etc.); callers can also
/// compose <see cref="TcrConnection.SendRequestAsync"/> with a builder
/// result directly when they want full control over reply dispatch.
/// </para>
/// <para>
/// All MessageTypes use <see cref="MetaTransactionId"/> = -1 unless they
/// participate in a Geode transaction (Phase 11+). Mirrors cppcache
/// <c>TcrMessage::writeHeader</c>: <c>m_txId = -1</c> when no
/// <c>TxState</c> is present.
/// </para>
/// </remarks>
internal sealed partial class TcrMessageBuilder
{
    IServiceProvider _serviceProvider;
    MessageType _messageType;
    int _transactionId = -1;
    byte _earlyAck = 0;
    readonly List<TcrPartBuilder> _tcrPartBuilders = [];

    private TcrMessageBuilder(IServiceProvider serviceProvider, MessageType messageType)
    {
        _serviceProvider = serviceProvider;
        _messageType = messageType;
    }

    public static TcrMessageBuilder Create(IServiceProvider serviceProvider, MessageType messageType)
    {
        return new TcrMessageBuilder(serviceProvider, messageType);
    }

    public TcrMessageBuilder AddKeepAlivePart(bool value)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.KeepAlive(value));
        return this;
    }

    public async ValueTask<TcrMessage> BuildAsync(CancellationToken ct = default)
    {
        var parts = new List<TcrPart>();
        foreach (var builder in _tcrPartBuilders)
        {
            parts.Add(await builder.BuildAsync(ct));
        }

        // Direct construction (record positional ctor) rather than
        // ActivatorUtilities — BuildAsync is called from CloseAsync on the
        // sp-teardown path, and ActivatorUtilities would re-enter the
        // disposing ServiceProvider to resolve `IServiceProvider`, throwing
        // ObjectDisposedException. We already hold every ctor arg.
        return new TcrMessage(_serviceProvider, _messageType, _transactionId, _earlyAck, parts);

    }
}

