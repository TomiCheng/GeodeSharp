namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="bool"/> ??/// <see cref="DSCode.CacheableBoolean"/> (53). Wire payload is 1
/// byte: <c>0</c> = false, non-zero = true. Mirrors cppcache
/// <c>CacheableBoolean</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class BooleanDataConverter : DataConverter<bool>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableBoolean };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(DataOutput writer, bool value, byte dsCode, int depth) =>
        writer.WriteByte(value ? (byte)1 : (byte)0);

    public override bool Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        reader.ReadByte() != 0;
}
