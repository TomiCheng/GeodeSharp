using Geode.Client.Internal;
using Geode.Client.Services;

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
    GeodeCache? _cache;
    ThinClientBaseDM? _dm;
    MessageType _messageType;
    int _transactionId = -1;
    byte _earlyAck = 0;

    private TcrMessageBuilder(IServiceProvider serviceProvider, MessageType messageType)
    {
        _serviceProvider = serviceProvider;
        _messageType = messageType;
    }

    public static TcrMessageBuilder Create(IServiceProvider serviceProvider, MessageType messageType)
    {
        return new TcrMessageBuilder(serviceProvider, messageType);
    }

    public TcrMessageBuilder SetPool(ThinClientBaseDM dm)
    {
        _dm = dm;
        return this;
    }

    public TcrMessageBuilder SetCache(GeodeCache cache)
    {
        _cache = cache;
        return this;
    }

    public ValueTask<TcrMessage> BuildAsync(CancellationToken ct = default)
    {
        return _messageType switch
        {
            MessageType.Ping => BuildPingAsync(ct),
            _ => throw new NotSupportedException($"{_messageType} not yet implemented")
        };
    }



    //private readonly IServiceProvider _serviceProvider = serviceProvider;

    ///// <summary>
    ///// Sentinel used for any request that isn't part of a Geode
    ///// transaction. Geode transactions land in Phase 11+.
    ///// </summary>
    //public const int MetaTransactionId = -1;

    //// partBuilder is consumed positionally by the operation partials
    //// (.Put / .Get / .ContainsKey / ...). serializationRegistry is the
    //// key/value codec dispatch ??partials use it to replace inline type
    //// guards with central registry lookup as each op is reworked.
    //private readonly SerializationRegistry _serializationRegistry = serializationRegistry;
}

