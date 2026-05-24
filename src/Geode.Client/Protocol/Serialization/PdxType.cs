namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// PDX class schema (ordered field list + server-assigned typeId).
/// </summary>
/// <remarks>
/// <see cref="Initialize"/> populates two field-index maps:
/// <list type="bullet">
///   <item><see cref="LocalToRemoteFieldMap"/><c>[localIdx]</c>:
///         <c>-2</c> = same position remotely, <c>N ≥ 0</c> = remote
///         sequence id, <c>-1</c> = local-only.</item>
///   <item><see cref="RemoteToLocalFieldMap"/><c>[remoteIdx]</c>:
///         <c>1</c> = present locally, <c>-1</c> = local missing
///         (var-len), <c>-2</c> = local missing (fixed).</item>
/// </list>
/// </remarks>
internal sealed class PdxType(
    PdxTypeRegistry pdxTypeRegistry,
    string className,
    IReadOnlyList<PdxField> fields)
{
    private readonly Dictionary<string, PdxField> _fieldByName = [];

    /// <summary>
    /// Stamp <see cref="PdxField.VarLenOffsetIndex"/> /
    /// <see cref="PdxField.RelativeOffset"/> on each field and build the
    /// name lookup. Mirror of cppcache <c>generatePositionMap</c>
    /// (<c>PdxType.cpp:489</c>).
    /// </summary>
    private void GeneratePositionMap()
    {
        // Pass 1 — back-to-front. Each fixed-size field anchors to the
        // nearest var-len behind it; var-len fields anchor to themselves.
        var foundVarLen = false;
        var lastVarLenSeqId = 0;
        PdxField? previousField = null;

        for (int i = Fields.Count - 1; i >= 0; i--)
        {
            var f = Fields[i];
            _fieldByName[f.Name] = f;

            if (!f.IsFixedSize)
            {
                f.VarLenOffsetIndex = f.VarLenFieldIdx;
                f.RelativeOffset = 0;
                foundVarLen = true;
                lastVarLenSeqId = f.VarLenFieldIdx;
            }
            else if (foundVarLen)
            {
                f.VarLenOffsetIndex = lastVarLenSeqId;
                f.RelativeOffset = -f.FixedSize + previousField!.RelativeOffset;
            }
            else
            {
                f.VarLenOffsetIndex = -1;                      // use PDX length header
                f.RelativeOffset = previousField is null
                    ? -f.FixedSize
                    : -f.FixedSize + previousField.RelativeOffset;
            }
            previousField = f;
        }

        // Pass 2 — front-to-back. Overwrite pass 1 for the fixed-size
        // prefix with absolute start-of-payload offsets.
        foundVarLen = false;
        var prevFixedSizeOffsets = 0;
        for (int i = 0; i < Fields.Count && !foundVarLen; i++)
        {
            var f = Fields[i];
            if (!f.IsFixedSize)
            {
                f.VarLenOffsetIndex = -1;                      // first var-len
                f.RelativeOffset = prevFixedSizeOffsets;
                foundVarLen = true;
            }
            else
            {
                f.VarLenOffsetIndex = 0;                       // no offset lookup needed
                f.RelativeOffset = prevFixedSizeOffsets;
                prevFixedSizeOffsets += f.FixedSize;
            }
        }
    }

    /// <summary>
    /// Build <see cref="LocalToRemoteFieldMap"/>. Mirror of cppcache
    /// <c>initLocalToRemote</c> (<c>PdxType.cpp:233</c>).
    /// </summary>
    private void InitLocalToRemote()
    {
        var localPdxType = pdxTypeRegistry.GetLocalPdxType(ClassName);
        if (localPdxType is null) return;

        var localFields = localPdxType.Fields;
        var map = new int[localFields.Count];

        // Phase 1: parallel walk while fields stay in the same order.
        var fieldIdx = 0;
        var commonCount = Math.Min(localFields.Count, Fields.Count);
        for (; fieldIdx < commonCount; fieldIdx++)
        {
            if (localFields[fieldIdx].SameField(Fields[fieldIdx]))
                map[fieldIdx] = -2;
            else
                break;
        }

        // Phase 2: order diverged — linear-search remote for the rest.
        for (; fieldIdx < localFields.Count; fieldIdx++)
        {
            var localField = localFields[fieldIdx];
            var found = false;
            foreach (var remoteField in Fields)
            {
                if (localField.SameField(remoteField))
                {
                    map[fieldIdx] = remoteField.Index;
                    found = true;
                    break;
                }
            }
            if (!found) map[fieldIdx] = -1;
        }

        LocalToRemoteFieldMap = map;
    }

    /// <summary>
    /// Build <see cref="RemoteToLocalFieldMap"/>. Mirror of cppcache
    /// <c>initRemoteToLocal</c> (<c>PdxType.cpp:165</c>).
    /// </summary>
    private void InitRemoteToLocal()
    {
        var localPdxType = pdxTypeRegistry.GetLocalPdxType(ClassName);
        if (localPdxType is null) return;

        var localFields = localPdxType.Fields;
        var map = new int[Fields.Count];
        NumberOfFieldsExtra = 0;

        for (int i = 0; i < Fields.Count; i++)
        {
            var remoteField = Fields[i];
            var foundLocal = false;
            foreach (var localField in localFields)
            {
                if (localField.SameField(remoteField))
                {
                    map[i] = 1;
                    foundLocal = true;
                    break;
                }
            }
            if (!foundLocal)
            {
                map[i] = remoteField.IsFixedSize ? -2 : -1;
                NumberOfFieldsExtra++;
            }
        }
        RemoteToLocalFieldMap = map;
    }

    /// <summary>O(1) field lookup by name; <see langword="null"/> on miss.</summary>
    internal PdxField? GetField(string name) => _fieldByName.GetValueOrDefault(name);

    internal int[]? LocalToRemoteFieldMap { get; private set; }
    internal int[]? RemoteToLocalFieldMap { get; private set; }
    internal int NumberOfFieldsExtra { get; private set; }

    /// <summary>
    /// Compute the read/write lookup tables. Called by
    /// <c>SerializationRegistry</c> Step A.3 (fresh local schema) and
    /// again when a remote schema arrives via <c>GetPdxTypeById</c>.
    /// Mirror of cppcache <c>InitializeType()</c> (<c>PdxType.cpp:300</c>).
    /// </summary>
    public void Initialize()
    {
        InitRemoteToLocal();    // write path
        InitLocalToRemote();    // read path
        GeneratePositionMap();  // byte-offset cache + name lookup
    }

    public string ClassName => className;

    public IReadOnlyList<PdxField> Fields => fields;

    /// <summary>Server-assigned typeId; <c>-1</c> until <c>GET_PDX_ID_FOR_TYPE</c> resolves.</summary>
    public int TypeId { get; set; } = -1;

    /// <summary>
    /// Wire-format Java class id for the PDX schema descriptor itself.
    /// Mirror of cppcache <c>PdxType::m_javaPdxClass</c>
    /// (<c>PdxType.cpp:37</c>).
    /// </summary>
    public const string JavaPdxClass = "org.apache.geode.pdx.internal.PdxType";

    /// <summary>
    /// <see langword="true"/> when <see cref="ClassName"/> names a Java
    /// (server-side) domain class; <see langword="false"/> for
    /// pure-PDX/<c>PdxInstance</c> schemas. Wire field at
    /// <c>PdxType::toData</c> is the negation (<c>noJavaClass</c>).
    /// Mirror of cppcache <c>is_java_class_</c>; default tracks
    /// cppcache's <c>expectDomainClass=true</c>.
    /// </summary>
    public bool IsJavaClass { get; init; } = true;

    /// <summary>
    /// cppcache <c>m_varLenFieldIdx</c>: the largest var-len slot id
    /// assigned, or <c>0</c> when no var-len fields are present.
    /// Mirror of cppcache <c>PdxType.cpp:151</c> — increments only after
    /// the first var-len field is added, so for N var-len fields the
    /// value is <c>max(0, N-1)</c>.
    /// </summary>
    public int VarLenFieldIdx
    {
        get
        {
            var count = 0;
            foreach (var f in Fields)
            {
                if (!f.IsFixedSize) count++;
            }
            return count == 0 ? 0 : count - 1;
        }
    }

    /// <summary>
    /// Serialise this schema into <paramref name="output"/> as the part
    /// body of a <c>GET_PDX_ID_FOR_TYPE</c> (opcode 93) request. Mirror
    /// of cppcache <c>PdxType::toData</c> (<c>PdxType.cpp:66</c>).
    /// </summary>
    /// <remarks>
    /// Wire layout (cppcache <c>PdxType.cpp:66-91</c>):
    /// <code>
    /// u8   DSCode.DataSerializable (45)
    /// u8   DSCode.Class (43)
    /// str  JavaPdxClass ("org.apache.geode.pdx.internal.PdxType")
    /// str  ClassName
    /// bool !IsJavaClass     (the wire field is "noJavaClass")
    /// i32  TypeId           (server reassigns; client sentinel is -1)
    /// i32  VarLenFieldIdx
    /// i32  Fields.Count     (DataOutput.WriteArrayLen)
    /// for each PdxField: PdxField.ToData(output)
    /// </code>
    /// <para>
    /// Per-field body is delegated to <see cref="PdxField.ToData"/>,
    /// currently a NIE leaf (cppcache <c>PdxFieldType.cpp:88</c>).
    /// </para>
    /// </remarks>
    public void ToData(DataOutput output)
    {
        output.WriteByte(DSCode.DataSerializable);
        output.WriteByte(DSCode.Class);
        output.WriteString(JavaPdxClass);
        output.WriteString(ClassName);
        output.WriteBool(!IsJavaClass);
        output.WriteInt32(TypeId);
        output.WriteInt32(VarLenFieldIdx);
        output.WriteArrayLen(Fields.Count);
        foreach (var f in Fields)
        {
            f.ToData(output);
        }
    }
}
