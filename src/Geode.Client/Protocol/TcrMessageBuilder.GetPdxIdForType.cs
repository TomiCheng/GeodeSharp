using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

partial class TcrMessageBuilder
{
    /// <summary>
    /// Build a <see cref="MessageType.GetPdxIdForType"/> (93) request frame.
    /// Mirror of cppcache <c>TcrMessageGetPdxIdForType</c> ctor
    /// (<c>cppcache/src/TcrMessage.cpp:2874</c>); the "send + reply" flow
    /// lives in <c>ThinClientPoolDM::GetPDXIdForType</c>
    /// (<c>cppcache/src/ThinClientPoolDM.cpp:900</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wire layout — Header (<see cref="MessageType.GetPdxIdForType"/>=93,
    /// NumParts=1, TransactionId=-1, EarlyAck=0) followed by:
    /// </para>
    /// <code>
    /// # Part    IsObject  Payload
    /// 1 Schema  1         PdxType body bytes (no leading "I'm a PDX" tag —
    ///                     cppcache writeObjectPart(..., callToData=true),
    ///                     i.e. SerializationRegistry::serializeWithoutHeader).
    ///                     The body itself opens with DSCode.DataSerializable (45)
    ///                     + DSCode.Class (43) + "org.apache.geode.pdx.internal.PdxType"
    ///                     — that's PdxType.ToData's own first bytes.
    /// </code>
    /// <para>
    /// Body serialisation is delegated to <see cref="PdxType.ToData"/>,
    /// which is currently a NIE leaf (cppcache <c>PdxType.cpp:66</c>).
    /// Until it lands, the builder itself wires up but any attempt to
    /// actually issue the request will surface the inner NIE.
    /// </para>
    /// </remarks>
    public async ValueTask<TcrMessage> GetPdxIdForTypeAsync(
        PdxType schema,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var parts = new List<TcrPart>(1)
        {
            // Part 1 — schema body, IsObject=1. PdxType.ToData writes its
            // own leading DSCode.DataSerializable byte, so partBuilder
            // does not prepend one. Mirror of cppcache
            // writeObjectPart(..., callToData=true) at TcrMessage.cpp:2880.
            await partBuilder.ObjectAsync(w =>
            {
                schema.ToData(w);
                return ValueTask.CompletedTask;
            }),
        };

        _ = ct;
        return ActivatorUtilities.CreateInstance<TcrMessage>(
            _serviceProvider, MessageType.GetPdxIdForType,
            MetaTransactionId, (byte)0, parts);
    }
}
