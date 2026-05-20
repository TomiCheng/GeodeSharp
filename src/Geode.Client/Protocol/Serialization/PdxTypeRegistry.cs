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

    /// <summary>
    /// 查本地採集過的 schema。回傳 <see langword="null"/> 表示此 className
    /// 還沒在本地 client 上採集過 — 也就是 <c>SerializationRegistry.TryWritePdx</c>
    /// Step A(第一次序列化)的觸發條件。
    /// </summary>
    /// <remarks>
    /// cppcache 對應 <c>PdxTypeRegistry::getLocalPdxType(className)</c>
    /// (<c>PdxTypeRegistry.cpp</c>),它另外維護 <c>localPdxTypes</c> map,
    /// 跟 <c>pdxTypes</c>(server 通知過來的 remote schema)分開。
    /// 我們目前還沒拆出 local map,所以先擺 NIE stub 把 call site 立起來,
    /// 真正實作等決定資料結構後再填(見 Step A 前置缺口)。
    /// </remarks>
    public PdxType? GetLocalPdxType(string className) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(GetLocalPdxType)}: " +
            $"local-vs-remote schema split not yet wired (Phase 2.1 Step A prereq).");

    /// <summary>
    /// 對齊 cppcache <c>PdxTypeRegistry::getPDXIdForType(type, pool, nType, checkIfThere)</c>
    /// (<c>PdxTypeRegistry.cpp:49-67</c>)。Step A.4 入口:
    /// <list type="number">
    ///   <item><paramref name="checkIfThere"/> 為 true → 先查本地 cache,有就回</item>
    ///   <item>否則打 <c>GET_PDX_ID_FOR_TYPE</c> wire op(透過 <paramref name="pool"/>)</item>
    ///   <item>把 typeId 設進 <paramref name="nType"/> 並 <c>AddPdxType</c></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// 前置缺口(全部 NIE 或缺):
    /// <list type="bullet">
    ///   <item><c>PdxType.ToData(DataOutput)</c> — schema 自我序列化(cppcache <c>PdxType::toData</c>)</item>
    ///   <item><c>TcrMessageBuilder.GetPdxIdForTypeAsync(PdxType)</c> — header(110, 1 part) + 1 個 ObjectPart</item>
    ///   <item><c>ThinClientPoolDM</c> 還沒收 ref;<c>IPool</c> 上也沒 SendSyncRequest API</item>
    ///   <item>Response 解析:<c>CacheableInt32</c>(DSCode 57 + 4 BE)→ typeId</item>
    /// </list>
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

    /// <summary>
    /// 把本地採集到的 schema 寫進 className→PdxType map。對應 cppcache
    /// <c>PdxTypeRegistry::addLocalPdxType</c>(寫入 <c>localPdxTypes_</c>)。
    /// </summary>
    /// <remarks>
    /// NIE stub — 等 local-vs-remote 兩個 dict 拆出來再實作(跟
    /// <see cref="GetLocalPdxType"/> 同一個前置缺口)。
    /// </remarks>
    public void AddLocalPdxType(string className, PdxType nType) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(AddLocalPdxType)}: " +
            $"local-vs-remote schema split not yet wired (Phase 2.1 Step A.7 prereq).");

    /// <summary>
    /// 把 typeId→PdxType 對應寫進 by-typeId map。對應 cppcache
    /// <c>PdxTypeRegistry::addPdxType</c>(寫入 <c>pdxTypes_</c>)。
    /// </summary>
    /// <remarks>
    /// NIE stub — 跟 <see cref="AddLocalPdxType"/> 對稱;暫不實作,等
    /// 整套 local-vs-remote / lock 策略決定再一起做。
    /// </remarks>
    public void AddPdxType(int typeId, PdxType nType) =>
        throw new NotImplementedException(
            $"{nameof(PdxTypeRegistry)}.{nameof(AddPdxType)}: " +
            $"local-vs-remote schema split not yet wired (Phase 2.1 Step A.7 prereq).");

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
