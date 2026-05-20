namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// One PDX field's schema metadata. Mirror of cppcache <c>PdxFieldType</c>
/// (<c>cppcache/src/PdxFieldType.hpp</c>) — name kept distinct from
/// the <see cref="PdxFieldType"/> enum.
/// </summary>
/// <param name="Name">Field name as written in <c>ToData</c>.</param>
/// <param name="Type">Wire type tag.</param>
/// <param name="Index">Field order within the schema (0-based).</param>
/// <param name="IsFixedSize">
/// <see langword="true"/> for primitives (no offset-table entry);
/// <see langword="false"/> for var-len fields (string, byte[], object, …).
/// </param>
internal sealed record PdxField(
    string Name,
    PdxFieldType Type,
    int Index,
    bool IsFixedSize);
