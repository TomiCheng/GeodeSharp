using System.Buffers.Binary;
using Geode.Client.Pdx;
using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Encodes a PDX object's payload while running user's
/// <see cref="IPdxSerializable{TSelf}.ToData"/>. Mirror of cppcache
/// <c>PdxLocalWriter</c> (<c>cppcache/src/PdxLocalWriter.hpp</c>).
/// </summary>
internal class PdxLocalWriter(IServiceProvider serviceProvider, GeodeCache cache)
    : IPdxWriter, IDisposable
{
    // Wire layout (excluding leading DSCode.PDX byte written by
    // SerializationRegistry.TryWritePdx):
    //   PdxLength (4 BE)
    //   TypeId    (4 BE)
    //   <field data, in schema order>
    //   <offset table for var-len fields, reverse order, first omitted;
    //    1 / 2 / 4 bytes per entry depending on payload length —
    //    cppcache PdxLocalWriter::writeOffsets>

    // Internal scratch DataOutput resolved via DI (consistency with the
    // rest of the wire codec). PDX field writes never recurse into
    // SerializationRegistry — DI hands us one but it sits unused; the
    // overhead is one field, accepted for uniform construction style.
    private readonly DataOutput _output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
    private readonly List<PdxField> _fields = [];
    private readonly List<int> _varLenOffsets = [];

    /// <summary>
    /// 子類用:把目前累積的 field list 包成 <see cref="PdxType"/>。對應
    /// cppcache <c>PdxWriterWithTypeCollector::getPdxLocalType()</c> 從 base
    /// 拿 <c>m_pdxType</c> 的動作 — 我們蒐 field 在 base 做,所以這裡幫子類
    /// 把它取出來。
    /// </summary>
    protected PdxType BuildSchema(string className)
    {
        return PdxType.Create(serviceProvider, cache, className, _fields);
    }
    private readonly StringDataConverter _stringConverter = new(cache);

    public IPdxWriter WriteBoolean(string fieldName, bool value)
    {
        AddFixedField(fieldName, PdxFieldType.Boolean);
        _output.WriteBool(value);
        return this;
    }

    public IPdxWriter WriteByte(string fieldName, sbyte value)
    {
        AddFixedField(fieldName, PdxFieldType.Byte);
        _output.WriteSByte(value);
        return this;
    }

    public IPdxWriter WriteChar(string fieldName, char value)
    {
        AddFixedField(fieldName, PdxFieldType.Char);
        _output.WriteUInt16(value);
        return this;
    }

    public IPdxWriter WriteShort(string fieldName, short value)
    {
        AddFixedField(fieldName, PdxFieldType.Short);
        _output.WriteInt16(value);
        return this;
    }

    public IPdxWriter WriteInt(string fieldName, int value)
    {
        AddFixedField(fieldName, PdxFieldType.Int);
        _output.WriteInt32(value);
        return this;
    }

    public IPdxWriter WriteLong(string fieldName, long value)
    {
        AddFixedField(fieldName, PdxFieldType.Long);
        _output.WriteInt64(value);
        return this;
    }

    public IPdxWriter WriteFloat(string fieldName, float value)
    {
        AddFixedField(fieldName, PdxFieldType.Float);
        _output.WriteFloat(value);
        return this;
    }

    public IPdxWriter WriteDouble(string fieldName, double value)
    {
        AddFixedField(fieldName, PdxFieldType.Double);
        _output.WriteDouble(value);
        return this;
    }

    public IPdxWriter WriteDate(string fieldName, DateTime value)
    {
        // Wire form: signed int64 BE = milliseconds since 1970-01-01 UTC
        // (Java Date(long)). Reject Unspecified to avoid silent local-tz
        // assumption — same rule as DateTimeDataConverter.
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => throw new ArgumentException(
                "DateTime with Kind=Unspecified cannot be serialised: " +
                "the wire form is UTC milliseconds and Unspecified would " +
                "force a silent local-timezone assumption.",
                nameof(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        var ms = (utc - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerMillisecond;

        AddFixedField(fieldName, PdxFieldType.Date);
        _output.WriteInt64(ms);
        return this;
    }

    public IPdxWriter WriteString(string fieldName, string? value)
    {
        // Push offset BEFORE writing so reader knows where this var-len
        // field starts. cppcache PdxLocalWriter::writeString calls
        // addOffset() before m_dataOutput->writeString.
        _varLenOffsets.Add(_output.WrittenCount);
        AddVarLenField(fieldName, PdxFieldType.String);

        if (value is null)
        {
            // cppcache writeString(nullptr) → DSCode.CacheableNullString (69).
            // Distinct from generic DSCode.NullObj (41) used by WriteObject(null).
            _output.WriteByte(DSCode.CacheableNullString);
            return this;
        }

        // Reuse Phase 1's StringDataConverter so max-length, DSCode
        // selection (ASCII / huge / mod UTF-8 / UTF-16) and payload
        // encoding stay symmetric with non-PDX strings。IPdxWriter 是 sync
        // 介面(user ToData 不會 await),而 StringDataConverter 在
        // async 化之後只剩 WriteAsync;但 string encoding 純 CPU、不會真
        // await,所以這裡 block 一下是 no-op。
        var dsCode = _stringConverter.GetDsCode(value);
        _output.WriteByte(dsCode);
        _stringConverter.WriteAsync(_output, value, dsCode, depth: 0, ct: default)
            .AsTask().GetAwaiter().GetResult();
        return this;
    }

    /// <summary>
    /// Finalize the field-data payload(field bytes + offset table)。對應
    /// cppcache <c>PdxLocalWriter::endObjectWriting</c> + <c>writeOffsets</c>
    /// — 但**不含** wire-level header(<c>DSCode.PDX</c>/length/typeId),
    /// 那是 caller(<c>SerializationRegistry.TryWritePdxAsync</c>)
    /// 寫到外層 <see cref="DataOutput"/>。
    /// </summary>
    public byte[] BuildPayload()
    {
        var fieldData = _output.WrittenSpan;

        // Offset table: numVarLen - 1 entries (first var-len's offset is
        // implicit at 0, so it's elided). cppcache PdxLocalWriter::
        // writeOffsets writes them in reverse order at the tail of the
        // payload, with width chosen from total payload length.
        int numEntries = Math.Max(0, _varLenOffsets.Count - 1);
        if (numEntries == 0)
        {
            return fieldData.ToArray();
        }

        var (width, totalLen) = PickOffsetWidth(fieldData.Length, numEntries);
        var payload = new byte[totalLen];
        fieldData.CopyTo(payload);

        int pos = fieldData.Length;
        for (int i = _varLenOffsets.Count - 1; i > 0; i--)
        {
            int offset = _varLenOffsets[i];
            switch (width)
            {
                case 1:
                    payload[pos] = (byte)offset;
                    pos += 1;
                    break;
                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(pos), (ushort)offset);
                    pos += 2;
                    break;
                default: // 4
                    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(pos), offset);
                    pos += 4;
                    break;
            }
        }

        return payload;
    }

    public void Dispose() => _output.Dispose();

    /// <summary>
    /// Pick the smallest offset-entry width that keeps the total payload
    /// (field data + offset table) within the chosen-width range.
    /// Mirrors cppcache <c>PdxLocalWriter::calculateLenWithOffsets</c>.
    /// </summary>
    private static (int Width, int TotalLen) PickOffsetWidth(int fieldDataLen, int numEntries)
    {
        int probe1 = fieldDataLen + numEntries;
        if (probe1 <= 0xFF) return (1, probe1);

        int probe2 = fieldDataLen + numEntries * 2;
        if (probe2 <= 0xFFFF) return (2, probe2);

        return (4, fieldDataLen + numEntries * 4);
    }

    private void AddFixedField(string name, PdxFieldType type) =>
        _fields.Add(new PdxField(name, type, Index: _fields.Count, IsFixedSize: true));

    private void AddVarLenField(string name, PdxFieldType type) =>
        _fields.Add(new PdxField(
            name, type, Index: _fields.Count, IsFixedSize: false,
            // _varLenOffsets is appended to BEFORE this call (see WriteString),
            // so Count-1 = the slot id just claimed for this field.
            VarLenFieldIdx: _varLenOffsets.Count - 1));
}

