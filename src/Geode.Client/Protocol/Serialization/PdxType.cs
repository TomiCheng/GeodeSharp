namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// PDX class schema (field list). Mirror of cppcache <c>PdxType</c>
/// (<c>cppcache/src/PdxType.hpp</c>). Phase 2.1 stub — just the minimum
/// <see cref="PdxLocalWriter"/> builds and a future <c>PdxTypeRegistry</c>
/// keys off; equality / hash / merging not yet wired.
/// </summary>
internal sealed class PdxType(string className, IReadOnlyList<PdxField> fields)
{
    public string ClassName { get; } = className;
    public IReadOnlyList<PdxField> Fields { get; } = fields;

    /// <summary>
    /// Server-assigned typeId; set by <c>PdxTypeRegistry</c> after the
    /// <c>AddPdxType</c> wire op completes. <c>-1</c> until resolved.
    /// </summary>
    public int TypeId { get; set; } = -1;
}
