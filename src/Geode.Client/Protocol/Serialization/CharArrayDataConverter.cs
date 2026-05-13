namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="char"/><c>[]</c> ↔
/// <see cref="DSCode.CharArray"/> (27). Wire payload is a VL-encoded
/// length (1 / 3 / 5 bytes) followed by 2 bytes big-endian per
/// element — each element is one Java <c>char</c> / UTF-16 code
/// unit. Mirrors cppcache <c>CharArray</c>
/// (<c>CacheableArrayPrimitive&lt;char16_t, CharArray&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// Same per-element wire shape as <see cref="CharacterDataConverter"/>
/// (DSCode 54): one big-endian u16. The array form just precedes the
/// element stream with a VL length prefix.
/// </para>
/// <para>
/// <b>Not a Key</b> — see <see cref="BooleanArrayDataConverter"/>.
/// <c>null</c> is intercepted as <see cref="DSCode.NullObj"/> by the
/// registry; <see cref="Array.Empty{T}"/> writes <c>[27, 0x00]</c>.
/// </para>
/// </remarks>
internal sealed class CharArrayDataConverter : DataConverter<char[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CharArray };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, char[] value, byte dsCode, int depth)
    {
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            writer.WriteUInt16(element);
        }
    }

    public override char[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<char>();
        }
        var array = new char[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = (char)reader.ReadUInt16();
        }
        return array;
    }
}
