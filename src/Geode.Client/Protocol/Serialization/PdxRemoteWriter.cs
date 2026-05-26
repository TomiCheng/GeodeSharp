using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Encodes a PDX object's payload using a remote (server-known) <c>PdxType</c>'s
/// field layout, plus any preserved unread fields. Mirror of cppcache
/// <c>PdxRemoteWriter</c>(<c>cppcache/src/PdxRemoteWriter.hpp</c>)。
/// </summary>
internal sealed class PdxRemoteWriter : PdxLocalWriter
{
    static readonly ObjectFactory<PdxRemoteWriter> _factoryByClassName
        = ActivatorUtilities.CreateFactory<PdxRemoteWriter>([typeof(GeodeCache), typeof(string)]);

    static readonly ObjectFactory<PdxRemoteWriter> _factoryByPdxType
        = ActivatorUtilities.CreateFactory<PdxRemoteWriter>(
            [typeof(GeodeCache), typeof(PdxType), typeof(PdxRemotePreservedData)]);

    public static PdxRemoteWriter Create(IServiceProvider serviceProvider,
        GeodeCache cache, string className)
        => _factoryByClassName(serviceProvider, [cache, className]);

    public static PdxRemoteWriter Create(IServiceProvider serviceProvider,
        GeodeCache cache, PdxType mergedPdxType, PdxRemotePreservedData preservedData)
        => _factoryByPdxType(serviceProvider, [cache, mergedPdxType, preservedData]);

    /// <summary>
    /// 沒有 preserved data 時用:caller 只給 className,後面要照本地 schema
    /// 寫 wire bytes。對應 cppcache
    /// <c>PdxRemoteWriter(DataOutput&amp;, std::string pdxClassName, PdxTypeRegistry)</c>。
    /// </summary>
    public PdxRemoteWriter(IServiceProvider serviceProvider, GeodeCache cache, string className)
        : base(serviceProvider, cache)
    {
        ClassName = className;
    }

    /// <summary>
    /// 物件帶有殘留 unread fields 時用:照 merged schema 寫 + 把 unread bytes
    /// 補回。對應 cppcache
    /// <c>PdxRemoteWriter(DataOutput&amp;, std::shared_ptr&lt;PdxType&gt;,
    /// std::shared_ptr&lt;PdxRemotePreservedData&gt;, PdxTypeRegistry)</c>。
    /// </summary>
    public PdxRemoteWriter(
        IServiceProvider serviceProvider,
        GeodeCache cache,
        PdxType mergedPdxType,
        PdxRemotePreservedData preservedData)
        : base(serviceProvider, cache)
    {
        MergedPdxType = mergedPdxType;
        PreservedData = preservedData;
        ClassName = mergedPdxType.ClassName;
    }

    public string ClassName { get; }

    /// <summary>
    /// Server 已知的 merged schema(local + remote 合併過)。只有「帶 preserved
    /// data」形態才會非 null。
    /// </summary>
    public PdxType? MergedPdxType { get; }

    /// <summary>前次 deserialize 留下來的 unread fields;沒有就 null。</summary>
    public PdxRemotePreservedData? PreservedData { get; }
}
