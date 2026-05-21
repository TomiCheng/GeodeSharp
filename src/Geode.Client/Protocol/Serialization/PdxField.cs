namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// One PDX field's schema metadata. Mirror of cppcache <c>PdxFieldType</c>
/// (<c>cppcache/src/PdxFieldType.hpp</c>) — name kept distinct from
/// the <see cref="PdxFieldType"/> enum.
/// </summary>
/// <param name="Name">Field name as written in <c>ToData</c>.</param>
/// <param name="Type">Wire type tag.</param>
/// <param name="Index">Field order within this schema (0-based).</param>
/// <param name="IsFixedSize">
/// <see langword="true"/> for primitives (no offset-table entry);
/// <see langword="false"/> for var-len fields (string, byte[], object, …).
/// </param>
/// <param name="VarLenFieldIdx">
/// Slot index in the trailing offset table for var-len fields (0-based,
/// in declaration order). Unused for fixed-size fields (pass <c>-1</c>).
/// </param>
internal sealed record PdxField(
    string Name,
    PdxFieldType Type,
    int Index,
    bool IsFixedSize,
    int VarLenFieldIdx = -1)
{
    /// <summary>
    /// Fixed wire width in bytes for fixed-size types; <c>0</c> for
    /// var-len types. Mirrors cppcache <c>PdxTypes::kPdxXxxSize</c>
    /// constants (<c>cppcache/src/PdxTypes.hpp</c>).
    /// </summary>
    public int FixedSize => Type switch
    {
        PdxFieldType.Boolean or PdxFieldType.Byte => 1,
        PdxFieldType.Char or PdxFieldType.Short => 2,
        PdxFieldType.Int or PdxFieldType.Float => 4,
        PdxFieldType.Long or PdxFieldType.Double or PdxFieldType.Date => 8,
        _ => 0,
    };

    /// <summary>
    /// Offset-table entry to read when locating this field on the wire;
    /// <c>-1</c> = use the PDX length header (field lives in the fixed
    /// suffix), <c>0</c> = no offset lookup needed (field lives in the
    /// fixed prefix before any var-len). Stamped by
    /// <see cref="PdxType.Initialize"/>'s position-map pass.
    /// </summary>
    public int VarLenOffsetIndex { get; set; }

    /// <summary>
    /// Signed delta to apply on top of the resolved offset. Lets
    /// fixed-size neighbours of a var-len anchor share its offset slot
    /// via constant adjustments. Stamped by
    /// <see cref="PdxType.Initialize"/>'s position-map pass.
    /// </summary>
    public int RelativeOffset { get; set; }

    /// <summary>
    /// Identity equality on (<see cref="Name"/>, <see cref="Type"/>,
    /// <see cref="IsFixedSize"/>) — ignores <see cref="Index"/> and the
    /// var-len indices so two schemas with reordered fields can still
    /// recognise "this is the same field". Used by
    /// <see cref="PdxType.Initialize"/> for the local↔remote field diff.
    /// Mirror of cppcache <c>PdxFieldType::equals</c>.
    /// </summary>
    public bool SameField(PdxField other) =>
        Name == other.Name && Type == other.Type && IsFixedSize == other.IsFixedSize;
}
