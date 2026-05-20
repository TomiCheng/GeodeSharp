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
/// </remarks>
internal sealed class PdxTypeRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, PdxType> _byTypeId = [];
    private readonly Dictionary<string, PdxType> _byClassName = [];

    /// <summary>
    /// Resolve the typeId for <paramref name="schema"/>. Returns the cached
    /// id when known; otherwise asks the server via
    /// <see cref="SendGetPdxIdForType"/> and caches the response.
    /// cppcache equivalent: <c>PdxTypeRegistry::getPDXIdForType</c>
    /// (<c>PdxTypeRegistry.cpp:49-67</c>).
    /// </summary>
    public int ResolveTypeId(PdxType schema)
    {
        lock (_gate)
        {
            if (_byClassName.TryGetValue(schema.ClassName, out var existing)
                && existing.TypeId > 0)
            {
                return existing.TypeId;
            }
        }

        var typeId = SendGetPdxIdForType(schema);
        Add(typeId, schema);
        return typeId;
    }

    /// <summary>
    /// Send <c>MessageType.GetPdxIdForType</c> wire op for an unknown
    /// schema and return the server-assigned typeId.
    /// </summary>
    /// <remarks>
    /// TODO Phase 2.1 wire op. Sub-steps:
    /// <list type="number">
    ///   <item>Inject <c>PoolManager</c> / <c>TcrConnectionManager</c> into this service.</item>
    ///   <item>Add <c>TcrMessageBuilder.GetPdxIdForType(schema)</c>
    ///         (cppcache <c>TcrMessage.cpp:2874</c> — header(GET_PDX_ID_FOR_TYPE, 1 part) +
    ///         writeObjectPart(schema, callToData=true) i.e. PdxType.toData
    ///         without DSCode header).</item>
    ///   <item><c>PdxType</c> needs to know how to serialise itself
    ///         (className, numFields, field metadata) — DataSerializableInternal
    ///         equivalent, DSFid=17.</item>
    ///   <item>sendSyncRequest, parse <c>CacheableInt32</c> response (DSCode 57 + 4 BE).</item>
    /// </list>
    /// cppcache reference: <c>ThinClientPoolDM::GetPDXIdForType</c>
    /// (<c>ThinClientPoolDM.cpp:900-932</c>).
    /// </remarks>
    private static int SendGetPdxIdForType(PdxType schema) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(SendGetPdxIdForType)}: " +
            $"GetPdxIdForType wire op not yet implemented (Phase 2.1) " +
            $"for className '{schema.ClassName}'.");

    /// <summary>Look up cached schema by typeId; <see langword="null"/> on miss.</summary>
    public PdxType? GetPdxType(int typeId)
    {
        lock (_gate)
        {
            return _byTypeId.TryGetValue(typeId, out var t) ? t : null;
        }
    }

    /// <summary>
    /// Cache a typeId ↔ schema mapping. Called after the server returns
    /// a typeId, or when a PDX payload arrives with an unknown schema.
    /// </summary>
    public void Add(int typeId, PdxType schema)
    {
        schema.TypeId = typeId;
        lock (_gate)
        {
            _byTypeId[typeId] = schema;
            _byClassName[schema.ClassName] = schema;
        }
    }
}
