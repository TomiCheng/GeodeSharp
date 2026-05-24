namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Holds the unread-field bytes a previous deserialize captured for a PDX
/// object, plus the merged typeId that schema lives under。Mirror of cppcache
/// <c>PdxRemotePreservedData</c>(<c>cppcache/src/PdxRemotePreservedData.hpp</c>)。
/// </summary>
/// <remarks>
/// 等 Step B 路徑接通(<c>PdxTypeRegistry.GetPreserveData</c> /
/// <c>SetPreserveData</c>)再把欄位填齊。目前只放 placeholder MergedTypeId,
/// 讓 Step B.2 的 <c>new PdxRemoteWriter(output, mergedPdxType,
/// preservedData, registry)</c> ctor 簽名能引到。
/// </remarks>
internal sealed class PdxRemotePreservedData
{
    public int MergedTypeId { get; init; } = -1;
}
