using System.Collections.Concurrent;
using Geode.Client.Internal;

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
internal sealed class PdxTypeRegistry
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
    /// First checks the local cache (when <paramref name="checkIfThere"/>
    /// is set); otherwise fires the GET_PDX_ID_FOR_TYPE wire op via
    /// <paramref name="pool"/>, sets the typeId on <paramref name="nType"/>,
    /// and adds it to <c>_byTypeId</c>. Mirror of cppcache
    /// <c>PdxTypeRegistry::getPDXIdForType(type, pool, nType, checkIfThere)</c>
    /// (<c>PdxTypeRegistry.cpp:49</c>).
    /// </summary>
    /// <remarks>
    /// Outstanding prereqs (each is its own NIE / missing piece):
    /// <list type="bullet">
    ///   <item><c>PdxType.ToData(DataOutput)</c> — schema self-serialize
    ///         (cppcache <c>PdxType::toData</c>, <c>DataSerializableInternal</c>
    ///         DSFid=17).</item>
    ///   <item><c>TcrMessageBuilder.GetPdxIdForTypeAsync(PdxType)</c> —
    ///         header(<c>GET_PDX_ID_FOR_TYPE</c>=110, 1 part) + ObjectPart
    ///         carrying the schema bytes.</item>
    ///   <item><c>IPool</c> still has no send-sync API; need a method or
    ///         a <c>ThinClientPoolDM</c> ref injected here.</item>
    ///   <item>Reply parse: <c>CacheableInt32</c> (DSCode 57 + 4 BE) → typeId.</item>
    /// </list>
    /// cppcache wire-op driver: <c>ThinClientPoolDM::GetPDXIdForType</c>
    /// (<c>ThinClientPoolDM.cpp:900</c>).
    /// </remarks>
    public ValueTask<int> GetPdxIdForTypeAsync(
        string className,
        IPool? pool,
        PdxType nType,
        bool checkIfThere,
        CancellationToken ct) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(GetPdxIdForTypeAsync)}: " +
            $"GET_PDX_ID_FOR_TYPE wire op not yet implemented (Phase 2.1 Step A.4) " +
            $"for className '{className}'.");

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
