using Geode.Client.Services;
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
            [typeof(PdxType), typeof(PdxRemotePreservedData)]);

    public static PdxRemoteWriter Create(IServiceProvider serviceProvider, string className)
        => _factoryByClassName(serviceProvider, [className]);

    public static PdxRemoteWriter Create(IServiceProvider serviceProvider,
        PdxType mergedPdxType, PdxRemotePreservedData preservedData)
        => _factoryByPdxType(serviceProvider, [mergedPdxType, preservedData]);

    /// <summary>
    /// 沒有 preserved data 時用:caller 只給 className,後面要照本地 schema
    /// 寫 wire bytes。對應 cppcache
    /// <c>PdxRemoteWriter(DataOutput&amp;, std::string pdxClassName, PdxTypeRegistry)</c>。
    /// </summary>
    public PdxRemoteWriter(IServiceProvider serviceProvider, string className)
        : base(serviceProvider)
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
        PdxType mergedPdxType,
        PdxRemotePreservedData preservedData)
        : base(serviceProvider)
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
