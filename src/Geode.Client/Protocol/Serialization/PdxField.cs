/*
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

    /// <summary>
    /// <see langword="true"/> when this field participates in the schema's
    /// identity hash/equality. Mirror of cppcache <c>m_isIdentityField</c>;
    /// defaults to <see langword="false"/> (cppcache <c>PdxFieldType.cpp:66</c>).
    /// </summary>
    /// <remarks>
    /// Identity-field marking is the user-facing
    /// <c>IPdxWriter.MarkIdentityField</c> opt-in (Phase 2.x). Until that
    /// API lands, no field is identity-marked and ToData writes
    /// <see langword="false"/> for all fields.
    /// </remarks>
    public bool IsIdentityField { get; init; }

    /// <summary>
    /// Serialise this field's schema entry into <paramref name="output"/>
    /// as part of <see cref="PdxType.ToData"/>'s field-table loop. Mirror
    /// of cppcache <c>PdxFieldType::toData</c>
    /// (<c>cppcache/src/PdxFieldType.cpp:88</c>).
    /// </summary>
    /// <remarks>
    /// Wire layout (cppcache <c>PdxFieldType.cpp:88-97</c>):
    /// <code>
    /// str  Name
    /// i32  Index            (cppcache m_sequenceId)
    /// i32  VarLenFieldIdx   (0 for fixed-size fields — see note below)
    /// i8   (sbyte)Type      (PdxFieldType enum value)
    /// i32  RelativeOffset
    /// i32  VarLenOffsetIndex (cppcache m_vlOffsetIndex)
    /// bool IsIdentityField
    /// </code>
    /// <para>
    /// <b>VarLenFieldIdx wire compat.</b> Our record uses <c>-1</c> as
    /// the default sentinel for fixed-size fields (semantic "no slot");
    /// cppcache stores <c>0</c> in the same case (<c>PdxType.cpp:138</c>
    /// last ctor arg). Wire shape has to match cppcache, so we project
    /// <c>-1</c> → <c>0</c> at this boundary.
    /// </para>
    /// </remarks>
    public void ToData(DataOutput output)
    {
        output.WriteString(Name);
        output.WriteInt32(Index);
        output.WriteInt32(IsFixedSize ? 0 : VarLenFieldIdx);
        output.WriteByte((byte)(sbyte)Type);
        output.WriteInt32(RelativeOffset);
        output.WriteInt32(VarLenOffsetIndex);
        output.WriteBool(IsIdentityField);
    }
}

*/