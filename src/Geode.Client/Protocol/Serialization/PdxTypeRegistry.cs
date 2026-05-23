/*
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
    public async ValueTask<int> GetPdxIdForTypeAsync(string className, IPool? pool, PdxType nType, bool checkIfThere,
        CancellationToken ct)
    {
        if (checkIfThere && GetLocalPdxType(className) is { TypeId: > 0 } lpdx)
        {
            return lpdx.TypeId;
        }

        var typeId = await SendGetPdxIdForTypeAsync(pool, nType, ct);
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
    private async ValueTask<int> SendGetPdxIdForTypeAsync(IPool? pool, PdxType nType, CancellationToken ct)
    {
        // Mirror cppcache ThinClientPoolDM::GetPDXIdForType entry log
        // (ThinClientPoolDM.cpp:902).
        logger.LogDebug(
            "GetPdxIdForType: className={ClassName} fields={FieldCount}",
            nType.ClassName, nType.Fields.Count);

        // S.1 Build the GET_PDX_ID_FOR_TYPE request frame
        //     (cppcache TcrMessageGetPdxIdForType ctor).
        var request = await BuildGetPdxIdForTypeRequestAsync(nType, ct);

        // S.2 Send sync via the pool and await the reply. cppcache uses
        //     sendSyncRequest; ours awaits the DM API region ops use.
        var reply = await SendSyncRequestAsync(pool, request, ct);

        // S.3 Server-side failure to register the schema → no recovery,
        //     surface as GeodeException. Mirror cppcache LOGDEBUG
        //     (ThinClientPoolDM.cpp:914) for trace parity.
        if (reply.MessageType == MessageType.Exception)
        {
            var preview = DecodeExceptionPreview(reply);
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
    /// Resolves <see cref="TcrMessageBuilder"/> via the service provider
    /// (rather than holding a direct field) to break the
    /// PdxTypeRegistry ↔ SerializationRegistry ↔ TcrMessageBuilder DI cycle.
    /// </summary>
    /// <remarks>
    /// Still propagates NIE from <c>TcrMessageBuilder.GetPdxIdForTypeAsync</c>
    /// — its body needs <c>PdxType.ToData(DataOutput)</c> to be implemented
    /// before it can serialise the schema.
    /// </remarks>
    private ValueTask<TcrMessage> BuildGetPdxIdForTypeRequestAsync(PdxType nType, CancellationToken ct) =>
        serviceProvider.GetRequiredService<TcrMessageBuilder>()
            .GetPdxIdForTypeAsync(nType, ct);

    /// <summary>
    /// Send <paramref name="request"/> through <paramref name="pool"/>'s
    /// DM and await the reply. Mirror of cppcache
    /// <c>SerializationRegistry::GetPDXIdForType</c>'s
    /// <c>dynamic_cast&lt;ThinClientPoolDM*&gt;(pool)</c> step
    /// (<c>cppcache/src/SerializationRegistry.cpp:545</c>): the send
    /// API lives on the DM half of the pool, not the public
    /// <see cref="IPool"/> surface, so we cast through.
    /// </summary>
    /// <remarks>
    /// Defaults <c>attemptFailover=true, isBackgroundThread=false</c>
    /// match the cppcache call site (no overrides in
    /// <c>ThinClientPoolDM::GetPDXIdForType</c>).
    /// </remarks>
    private static async ValueTask<TcrMessage> SendSyncRequestAsync(
        IPool? pool,
        TcrMessage request,
        CancellationToken ct)
    {
        if (pool is null)
        {
            throw new GeodeException(
                "GET_PDX_ID_FOR_TYPE: no pool on the DataOutput context — " +
                "PDX serialise needs a pool. Mirror of cppcache " +
                "SerializationRegistry.cpp:551 IllegalStateException.");
        }

        // Cast to the DM half. IPool's only production impl is
        // ThinClientPoolDM, which multi-inherits ThinClientBaseDM where
        // SendSyncRequestAsync is declared. Same dispatch shape cppcache
        // uses (dynamic_cast). If a test double / future alt-pool
        // doesn't derive ThinClientBaseDM, fail loud rather than silently
        // dropping the wire op.
        if (pool is not ThinClientBaseDM dm)
        {
            throw new GeodeException(
                $"GET_PDX_ID_FOR_TYPE: pool {pool.GetType().Name} is not " +
                "ThinClientBaseDM-derived; cannot route the wire op. " +
                "Mirror of cppcache SerializationRegistry.cpp:547.");
        }

        return await dm.SendSyncRequestAsync(request, ct: ct);
    }

    /// <summary>
    /// Parse a one-part reply whose payload is a <c>CacheableInt32</c>
    /// (DSCode <c>57</c> + 4 BE bytes). Wire shape is small enough that
    /// inlining the decode here is cheaper than introducing a DI cycle
    /// to reach <c>SerializationRegistry.ReadObjectAsync</c>.
    /// </summary>
    private static ValueTask<int> ParseInt32ReplyAsync(TcrMessage reply, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (reply.Parts.Count < 1)
        {
            throw new GeodeException($"GET_PDX_ID_FOR_TYPE reply: expected 1 part, got {reply.Parts.Count}.");
        }

        var reader = new BigEndianBinaryReader(reply.Parts[0].Payload);
        var dsCode = reader.ReadByte();
        if (dsCode != DSCode.CacheableInt32)
        {
            throw new GeodeException($"GET_PDX_ID_FOR_TYPE reply: expected DSCode CacheableInt32 " +
                $"({DSCode.CacheableInt32}), got {dsCode}.");
        }

        return ValueTask.FromResult(reader.ReadInt32());
    }

    /// <summary>Short string from a <c>MessageType.Exception</c> reply for diagnostics.</summary>
    private static string DecodeExceptionPreview(TcrMessage reply) =>
        TcrMessageHelper.DecodeExceptionPreview(reply);

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

*/