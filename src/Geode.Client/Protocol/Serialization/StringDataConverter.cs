using Geode.Client.Services;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="string"/> ??four
/// wire DSCodes plus the null-string sentinel. Mirrors cppcache
/// <c>CacheableString</c> (<c>cppcache/src/CacheableString.cpp</c>)
/// and the dispatch logic in
/// <c>DataOutput::writeString</c> (<c>cppcache/include/geode/DataOutput.hpp:273</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Four encode forms, one converter</b> ??the only Tier A
/// converter that returns different DSCodes for different values.
/// The choice is made by <see cref="GetDsCode"/> after a single
/// content scan; <see cref="Write"/> branches on the chosen DSCode
/// without rescanning.
/// </para>
/// <list type="table">
///   <listheader>
///     <term>DSCode</term><description>Encode form</description>
///   </listheader>
///   <item>
///     <term>87 <c>CacheableASCIIString</c></term>
///     <description>u16 char-count + ASCII bytes. Picked when every
///     char is in 0x01..0x7F and char count ??65535.</description>
///   </item>
///   <item>
///     <term>88 <c>CacheableASCIIStringHuge</c></term>
///     <description>u32 char-count + ASCII bytes. Picked when every
///     char is ASCII but char count exceeds 65535.</description>
///   </item>
///   <item>
///     <term>42 <c>CacheableString</c></term>
///     <description>u16 byte-count + Java modified UTF-8 bytes.
///     Picked when content has non-ASCII (or NUL) chars and the
///     encoded byte length fits in u16.</description>
///   </item>
///   <item>
///     <term>89 <c>CacheableStringHuge</c></term>
///     <description>u32 char-count + UTF-16 BE chars. Picked when
///     content has non-ASCII and modified-UTF-8 byte length would
///     exceed 65535. <b>This DSCode does NOT use modified UTF-8</b>
///     ??it switches to UTF-16 BE because the length prefix unit
///     also changes from "bytes" to "chars". Matches cppcache
///     <c>writeUtf16Huge</c>.</description>
///   </item>
///   <item>
///     <term>69 <c>CacheableNullString</c></term>
///     <description>No payload. cppcache writes this for null in
///     known-type-string slots; we never produce it on write
///     (registry intercepts null with <see cref="DSCode.NullObj"/>
///     before reaching this converter) but accept it on read for
///     server-compat.</description>
///   </item>
/// </list>
/// <para>
/// <b>Modified UTF-8 vs standard UTF-8</b>: NUL is encoded as
/// <c>0xC0 0x80</c> (2 bytes, not 1); supplementary code points
/// arrive as a surrogate pair of two 3-byte sequences (6 bytes
/// total) rather than the 4-byte UTF-8 form. We cannot reuse
/// <see cref="System.Text.Encoding.UTF8"/> ??hand-rolled in
/// <see cref="DataOutput.WriteJavaModifiedUtf8"/> /
/// <see cref="DataInput.ReadJavaModifiedUtf8"/>.
/// </para>
/// </remarks>
internal sealed class StringDataConverter(GeodeCache cache)
    : DataConverter<string>
{
    // 87/88/42/89 cover the four encode forms; 69 is read-only
    // tolerance for null-string sentinels coming from the server.
    private static readonly byte[] _dsCodes =
    {
        DSCode.CacheableASCIIString,        // 87
        DSCode.CacheableASCIIStringHuge,    // 88
        DSCode.CacheableString,             // 42
        DSCode.CacheableStringHuge,         // 89
        DSCode.CacheableNullString,         // 69 ??decode-only
    };

    /// <summary>
    /// Snapshot of <see cref="Options.SerializationOptions.MaxStringLength"/>.
    /// Unit is whichever length the chosen DSCode encodes (chars for
    /// 87 / 88 / 89, modified-UTF-8 bytes for 42); same numeric cap
    /// applies to all four for simplicity. Snapshotted at ctor for
    /// the same reason as the array converters' <c>_maxArrayLength</c>.
    /// </summary>
    private readonly int _maxStringLength
        = cache.CacheProperties.MaxStringLength;

    public override byte[] DsCodes => _dsCodes;

    /// <summary>
    /// Pick which of the four encode DSCodes to emit for
    /// <paramref name="value"/>. Algorithm matches cppcache
    /// <c>DataOutput::writeString</c>: count chars, add per-char
    /// extra bytes for non-ASCII, then dispatch on (isAscii ? isHuge).
    /// </summary>
    public override byte GetDsCode(string value)
    {
        var charLen = value.Length;
        var utfLen = charLen;
        foreach (var c in value)
        {
            if (c >= 0x0001 && c <= 0x007F)
            {
                // 1-byte ASCII path ??already counted by charLen.
            }
            else if (c > 0x07FF)
            {
                // 3-byte modified-UTF-8 char.
                utfLen += 2;
            }
            else
            {
                // 2-byte modified-UTF-8 char (covers NUL via 0xC0 0x80
                // and the 0x80..0x7FF range).
                utfLen += 1;
            }
        }

        var isAscii = (utfLen == charLen);
        if (!isAscii)
        {
            return utfLen > 0xFFFF
                ? DSCode.CacheableStringHuge        // 89 ??UTF-16 BE
                : DSCode.CacheableString;           // 42 ??mod UTF-8
        }
        return charLen > 0xFFFF
            ? DSCode.CacheableASCIIStringHuge       // 88 ??ASCII huge
            : DSCode.CacheableASCIIString;          // 87 ??ASCII short
    }

    public override ValueTask WriteAsync(DataOutput writer, string value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _maxStringLength)
        {
            throw new InvalidOperationException(
                $"StringDataConverter: cannot serialise a string of {value.Length} chars "
                + $"— exceeds Serialization.MaxStringLength ({_maxStringLength}).");
        }
        switch (dsCode)
        {
            case DSCode.CacheableASCIIString:
                writer.WriteUInt16((ushort)value.Length);
                WriteAsciiBytes(writer, value);
                break;

            case DSCode.CacheableASCIIStringHuge:
                writer.WriteInt32(value.Length);
                WriteAsciiBytes(writer, value);
                break;

            case DSCode.CacheableString:
                writer.WriteJavaModifiedUtf8(value);
                break;

            case DSCode.CacheableStringHuge:
                writer.WriteInt32(value.Length);
                foreach (var c in value)
                {
                    writer.WriteUInt16(c);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dsCode),
                    dsCode,
                    $"StringDataConverter cannot write payload for DSCode {dsCode}; " +
                    $"GetDsCode only emits 42 / 87 / 88 / 89.");
        }
        return ValueTask.CompletedTask;
    }

    public override string? Read(DataInput reader, byte dsCode, int depth)
    {
        switch (dsCode)
        {
            case DSCode.CacheableASCIIString:
                {
                    // u16 length is wire-bounded to 65535 (already a
                    // ~130KB allocation max). Still apply MaxStringLength
                    // so a user who tightened the cap to e.g. 100 sees
                    // it honoured on every variant.
                    int length = reader.ReadUInt16();
                    EnsureStringLength(length);
                    return ReadAsciiBytes(reader, length);
                }

            case DSCode.CacheableASCIIStringHuge:
                {
                    // i32 length is the primary attack surface ??can be
                    // pinned at int.MaxValue by a hostile server.
                    int length = reader.ReadInt32();
                    EnsureStringLength(length);
                    return ReadAsciiBytes(reader, length);
                }

            case DSCode.CacheableString:
                // u16 byte-length is wire-bounded to 65535 ??at most
                // a ~130KB char[] inside ReadJavaModifiedUtf8. Below
                // any reasonable MaxStringLength so we skip the check
                // here rather than refactor ReadJavaModifiedUtf8 to
                // surface its internal length.
                return reader.ReadJavaModifiedUtf8();

            case DSCode.CacheableStringHuge:
                {
                    int charCount = reader.ReadInt32();
                    if (charCount == 0) return string.Empty;
                    EnsureStringLength(charCount);
                    var chars = new char[charCount];
                    for (var i = 0; i < charCount; i++)
                    {
                        chars[i] = (char)reader.ReadUInt16();
                    }
                    return new string(chars);
                }

            case DSCode.CacheableNullString:
                // cppcache typed-string-slot null sentinel. Registry
                // produces 41 (NullObj) for nulls; we tolerate 69 on
                // read for server-side compat.
                return null;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(dsCode),
                    dsCode,
                    $"StringDataConverter cannot read payload for DSCode {dsCode}.");
        }
    }

    private void EnsureStringLength(int length)
    {
        if (length > _maxStringLength)
        {
            throw new GeodeException(
                $"StringDataConverter: wire string length {length} exceeds "
                + $"Serialization.MaxStringLength ({_maxStringLength}) ??refusing to allocate.");
        }
    }

    /// <summary>
    /// Write the body of an ASCII-encoded string ??one byte per
    /// char, no length prefix (caller has already written it).
    /// </summary>
    private static void WriteAsciiBytes(DataOutput writer, string value)
    {
        // The cppcache path masks each char with 0x7F ("blindly assumes
        // ASCII"); GetDsCode already verified every char is in
        // 0x01..0x7F before picking an ASCII DSCode, so no masking is
        // needed ??the cast is exact.
        foreach (var c in value)
        {
            writer.WriteByte((byte)c);
        }
    }

    /// <summary>
    /// Read <paramref name="count"/> ASCII bytes as a string. Each
    /// byte becomes one <see cref="char"/> via direct widening.
    /// </summary>
    private static string ReadAsciiBytes(DataInput reader, int count)
    {
        if (count == 0) return string.Empty;
        var chars = new char[count];
        for (var i = 0; i < count; i++)
        {
            chars[i] = (char)reader.ReadByte();
        }
        return new string(chars);
    }
}
