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
/// <c>TcrMessagePut</c>, <c>TcrMessageRequest</c>, ...) — same
/// per-operation recipe, expressed as functions returning an immutable
/// <see cref="TcrMessage"/> rather than as a class hierarchy.
/// </para>
/// <para>
/// Pure functions: no I/O, no hidden state. The "send it + handle the
/// reply" half lives separately on <see cref="TcrConnection"/>
/// extensions (<see cref="Operations.PingExtensions"/> etc.); callers
/// can also compose <see cref="TcrConnection.SendRequestAsync"/> with a
/// builder result directly when they want full control over reply
/// dispatch.
/// </para>
/// <para>
/// All MessageTypes use <see cref="MetaTransactionId"/> = -1 unless they
/// participate in a Geode transaction (Phase 11+). Mirrors cppcache
/// <c>TcrMessage::writeHeader</c>: <c>m_txId = -1</c> when no
/// <c>TxState</c> is present.
/// </para>
/// </remarks>
internal sealed partial class TcrMessageBuilder(TcrPartBuilder partBuilder)
{
    /// <summary>
    /// Sentinel used for any request that isn't part of a Geode
    /// transaction. Geode transactions land in Phase 11+.
    /// </summary>
    public const int MetaTransactionId = -1;
}
