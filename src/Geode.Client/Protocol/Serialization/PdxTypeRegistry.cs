using System.Collections.Concurrent;
using Geode.Client.Internal;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Per-cache cache of PDX schema (<see cref="PdxType"/>) ↔ server-assigned
/// typeId. Scoped service. Mirror of cppcache <c>PdxTypeRegistry</c>
/// (<c>cppcache/src/PdxTypeRegistry.hpp</c>).
/// </summary>
/// <remarks>
/// Distinct from <c>Services.TypeRegistry</c> (user API — registers .NET
/// types). This is internal wire bookkeeping: typeId is server-assigned
/// per cluster, so the cache is scoped to one cache instance.
/// <para>
/// Two separate maps, mirroring cppcache:
/// <list type="bullet">
///   <item><c>_localByClassName</c> — schemas collected locally on this
///         client (first-time serialize path). Drives the Step A vs Step B
///         branch in <c>TryWritePdxAsync</c>.</item>
///   <item><c>_byTypeId</c> — every known schema keyed by server typeId,
///         including ones the server told us about during deserialize but
///         we never built locally.</item>
///   <item>A schema can live in <c>_byTypeId</c> only (server-known, no
///         local toData yet), <c>_localByClassName</c> only (collected but
///         typeId not assigned yet), or both (fully resolved).</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class PdxTypeRegistry(
    ILogger<PdxTypeRegistry> logger,
    // IServiceProvider used as a service locator to break the
    // PdxTypeRegistry ↔ SerializationRegistry ↔ TcrMessageBuilder DI cycle.
    // Only the GET_PDX_ID_FOR_TYPE wire-op path resolves anything; the
    // hot caches (Get/AddLocalPdxType / Get/AddPdxType) don't touch it.
    IServiceProvider serviceProvider)
{

    // ConcurrentDictionary: reads (GetLocalPdxType / GetPdxType) are hot —
    // every PDX serialize hits at least one of them. Writes (AddLocalPdxType
    // / AddPdxType) only fire once per unique className at Step A.7. Striped
    // locks give us lock-free reads; no cross-map atomicity is required so
    // each map locks independently.
    private readonly ConcurrentDictionary<int, PdxType> _byTypeId = new();
    private readonly ConcurrentDictionary<string, PdxType> _localByClassName = new();

    /// <summary>
    /// Insert a locally-collected schema into the className map.
    /// Mirror of cppcache <c>PdxTypeRegistry::addLocalPdxType</c>
    /// (<c>PdxTypeRegistry.cpp:138</c>).
    /// </summary>
    public void AddLocalPdxType(string className, PdxType nType) =>
        _localByClassName[className] = nType;

    /// <summary>
    /// Insert a typeId→schema mapping. Mirror of cppcache
    /// <c>PdxTypeRegistry::addPdxType</c> (<c>PdxTypeRegistry.cpp:123</c>).
    /// </summary>
    public void AddPdxType(int typeId, PdxType nType) =>
        _byTypeId[typeId] = nType;

    public PdxType? GetLocalPdxType(string className) =>
        _localByClassName.TryGetValue(className, out var t) ? t : null;

    /// <summary>
    /// Resolve the server-assigned typeId for a freshly-built local schema.
    /// Mirror of cppcache <c>PdxTypeRegistry::getPDXIdForType(type, pool,
    /// nType, checkIfThere)</c> (<c>PdxTypeRegistry.cpp:49</c>) +
    /// <c>ThinClientPoolDM::GetPDXIdForType</c>
    /// (<c>ThinClientPoolDM.cpp:900</c>).
    /// </summary>
    public async ValueTask<int> GetPdxIdForTypeAsync(string className, ThinClientBaseDM dm, PdxType nType, bool checkIfThere,
        CancellationToken ct)
    {
        if (checkIfThere && GetLocalPdxType(className) is { TypeId: > 0 } lpdx)
        {
            return lpdx.TypeId;
        }

        var typeId = await SendGetPdxIdForTypeAsync(dm, nType, ct);
        AddPdxType(typeId, nType);
        return typeId;
    }

    /// <summary>
    /// Wire-op chunk of <see cref="GetPdxIdForTypeAsync"/> (G.2–G.4).
    /// Mirror of cppcache <c>ThinClientPoolDM::GetPDXIdForType</c>
    /// (<c>ThinClientPoolDM.cpp:900</c>).
    /// </summary>
    /// <remarks>
    /// Three prereqs:
    /// <list type="bullet">
    ///   <item><c>PdxType.ToData(DataOutput)</c> — schema self-serialize.</item>
    ///   <item><c>TcrMessageBuilder.GetPdxIdForTypeAsync(PdxType, ct)</c> —
    ///         header(<c>GET_PDX_ID_FOR_TYPE</c>=93, 1 part) +
    ///         ObjectPart carrying the schema bytes.</item>
    ///   <item><c>IPool.SendSyncRequestAsync</c> — single endpoint send
    ///         + await reply, throwing on <c>MessageType.Exception</c>.</item>
    /// </list>
    /// </remarks>
    private async ValueTask<int> SendGetPdxIdForTypeAsync(ThinClientBaseDM dm, PdxType nType, CancellationToken ct)
    {
        logger.LogDebug("GetPdxIdForType: className={ClassName} fields={FieldCount}", nType.ClassName, nType.Fields.Count);

        // S.1 Build the GET_PDX_ID_FOR_TYPE request frame
        //     (cppcache TcrMessageGetPdxIdForType ctor).
        var request = await BuildGetPdxIdForTypeRequestAsync(nType, ct);

        // S.2 Send sync via the pool and await the reply. cppcache uses
        //     sendSyncRequest; ours awaits the DM API region ops use.
        var reply = await dm.SendSyncRequestAsync(request, ct: ct);

        // S.3 Server-side failure to register the schema → no recovery,
        //     surface as GeodeException. Mirror cppcache LOGDEBUG
        //     (ThinClientPoolDM.cpp:914) for trace parity.
        if (reply.MessageType == MessageType.Exception)
        {
            var preview = TcrMessageHelper.DecodeExceptionPreview(reply);
            logger.LogDebug("GetPdxIdForType: server exception for className={ClassName}: {Exception}",
                nType.ClassName, preview);
            throw new GeodeException("GET_PDX_ID_FOR_TYPE failed: " + preview);
        }

        // S.4 Reply carries a single ObjectPart whose payload is a
        //     CacheableInt32 (DSCode 57 + 4 BE).
        return await ParseInt32ReplyAsync(reply, ct);
    }

    // ── S.* leaves (each maps to a missing prereq) ────────────────────

    /// <summary>
    /// Build the <c>GET_PDX_ID_FOR_TYPE</c> (opcode 93) request frame:
    /// header(1 part) + ObjectPart carrying <c>PdxType.ToData</c>'s bytes.
    /// Mirror of cppcache <c>TcrMessageGetPdxIdForType</c> ctor
    /// (<c>cppcache/src/TcrMessage.cpp:2874</c>).
    /// </summary>
    /// <remarks>
    /// Single ObjectPart, <c>IsObject=1</c>; body comes from
    /// <see cref="PdxType.ToData"/>, which writes its own leading
    /// <c>DSCode.DataSerializable</c> + <c>DSCode.Class</c> +
    /// <c>"org.apache.geode.pdx.internal.PdxType"</c>. Mirror of cppcache
    /// <c>writeObjectPart(pdxType, isDelta=false, callToData=true)</c>
    /// (<c>TcrMessage.cpp:2883</c>).
    /// </remarks>
    private ValueTask<TcrMessage> BuildGetPdxIdForTypeRequestAsync(PdxType nType, CancellationToken ct) =>
        TcrMessageBuilder
            .Create(serviceProvider, MessageType.GetPdxIdForType)
            .AddPart(_ =>
            {
                using var output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
                nType.ToData(output);
                return new ValueTask<TcrPart>(new TcrPart(IsObject: 1, output.WrittenSpan.ToArray()));
            })
            .BuildAsync(ct);

    /// <summary>
    /// Parse a one-part reply whose payload is a raw 4-byte BE <c>int</c>.
    /// Mirror of cppcache <c>TcrMessage::readIntPart</c>
    /// (<c>cppcache/src/TcrMessage.cpp:419</c>): the wire carries no DSCode
    /// — <c>m_value = CacheableInt32::create(typeId)</c> downstream is an
    /// in-memory wrap, not part of the wire format.
    /// </summary>
    private static ValueTask<int> ParseInt32ReplyAsync(TcrMessage reply, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (reply.Parts.Count < 1)
        {
            throw new GeodeException($"GET_PDX_ID_FOR_TYPE reply: expected 1 part, got {reply.Parts.Count}.");
        }

        var part = reply.Parts[0];
        if (part.IsObject != 0)
        {
            throw new GeodeException($"GET_PDX_ID_FOR_TYPE reply: expected non-object int part, got IsObject={part.IsObject}.");
        }
        if (part.Payload.Length != 4)
        {
            throw new GeodeException($"GET_PDX_ID_FOR_TYPE reply: expected 4-byte int payload, got {part.Payload.Length} bytes.");
        }

        var reader = new DataInput(part.Payload);
        return ValueTask.FromResult(reader.ReadInt32());
    }

    /// <summary>Look up cached schema by typeId; <see langword="null"/> on miss.</summary>
    public PdxType? GetPdxType(int typeId) =>
        _byTypeId.TryGetValue(typeId, out var t) ? t : null;

    /// <summary>
    /// Look up unread-field bytes preserved from a previous deserialize.
    /// Returns <see langword="null"/> when the object has no preserved data
    /// (the common case for a freshly constructed user object). Mirror of
    /// cppcache <c>PdxTypeRegistry::getPreserveData</c>
    /// (<c>PdxTypeRegistry.cpp:202</c>).
    /// </summary>
    /// <remarks>
    /// NIE until the read side calls <c>SetPreserveData</c>; once that path
    /// exists, this lookup just queries the preserved-data map.
    /// </remarks>
    public PdxRemotePreservedData? GetPreserveData(object value) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(GetPreserveData)}: " +
            $"preserve-data tracking not yet wired (Phase 2.1 Step B.1 prereq;" +
            $" needs SetPreserveData on the read side first).");

}
