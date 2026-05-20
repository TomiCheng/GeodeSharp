namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Encodes a PDX object while simultaneously collecting its field layout into
/// a fresh <c>PdxType</c> (used on first serialization of an unknown type).
/// Mirror of cppcache <c>PdxWriterWithTypeCollector</c>
/// (<c>cppcache/src/PdxWriterWithTypeCollector.hpp</c>).
/// </summary>
internal sealed class PdxWriterWithTypeCollector(IServiceProvider serviceProvider, string className)
    : PdxLocalWriter(serviceProvider)
{
    // cppcache PdxWriterWithTypeCollector ctor 帶 className 進來,塞到
    // m_pdxClassName。我們先把 className 留著,後面 Step A.3 / A.7 採集 schema
    // 跟 register 到 PdxTypeRegistry 時都會用到。
    public string ClassName { get; } = className;

    /// <summary>
    /// 把 user ToData 期間蒐集到的 field list 包成 <see cref="PdxType"/>。
    /// 對應 cppcache <c>PdxWriterWithTypeCollector::getPdxLocalType()</c>。
    /// </summary>
    public PdxType GetPdxLocalType() => BuildSchema(ClassName);
}
