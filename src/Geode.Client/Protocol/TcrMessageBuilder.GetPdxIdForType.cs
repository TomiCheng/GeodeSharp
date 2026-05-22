using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    /// </remarks>
    public ValueTask<TcrMessage> GetPdxIdForTypeAsync(
        PdxType schema,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // TODO: PdxType.ToData(DataOutput) not implemented yet — the part
        //       body that should run is `schema.ToData(w)` inside the
        //       partBuilder.ObjectAsync lambda. Until that lands, this
        //       builder cannot produce a real request.
        //
        // Intended shape once PdxType.ToData exists:
        //
        //   _logger.LogDebug(
        //       "TcrMessageBuilder.GetPdxIdForTypeAsync: className={ClassName}",
        //       schema.ClassName);
        //   var parts = new List<TcrPart>(1)
        //   {
        //       // Part 1 — schema body, IsObject=1, no DSCode prefix added by
        //       //          partBuilder (PdxType.ToData writes its own leading
        //       //          DSCode.DataSerializable byte).
        //       await partBuilder.ObjectAsync(w =>
        //       {
        //           schema.ToData(w);
        //           return ValueTask.CompletedTask;
        //       }),
        //   };
        //   return ActivatorUtilities.CreateInstance<TcrMessage>(
        //       _serviceProvider, MessageType.GetPdxIdForType,
        //       MetaTransactionId, (byte)0, parts);

        _ = ct;
        throw new NotImplementedException(
            $"{nameof(TcrMessageBuilder)}.{nameof(GetPdxIdForTypeAsync)}: " +
            "PdxType.ToData(DataOutput) not implemented yet — request body " +
            "can't be serialised. See cppcache TcrMessage.cpp:2874 / PdxType.cpp:66.");
    }
}
