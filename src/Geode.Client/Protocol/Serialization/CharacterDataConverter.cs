namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="char"/> ??/// <see cref="DSCode.CacheableCharacter"/> (54). Wire payload is 2
/// bytes big-endian (UTF-16 code unit, 0..65535). Mirrors cppcache
/// <c>CacheableCharacter</c>
/// (<c>cppcache/src/CacheableBuiltins.cpp</c> <c>toData</c> /
/// <c>fromData</c>) which serialises <c>char16_t</c> as <c>u16</c>.
/// </summary>
/// <remarks>
/// Java <c>char</c> is a UTF-16 code unit (unsigned 16-bit) and so is
/// .NET <see cref="char"/> ??one-to-one mapping, no surrogate pairs
/// handled at this layer (a single char can be an unpaired surrogate
/// half; that's the caller's concern, the wire just carries the
/// code unit).
/// </remarks>
internal sealed class CharacterDataConverter : DataConverter<char>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableCharacter };

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, char value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteUInt16(value);
        return ValueTask.CompletedTask;
    }

    public override char Read(DataInput reader, byte dsCode, int depth) =>
        (char)reader.ReadUInt16();
}
