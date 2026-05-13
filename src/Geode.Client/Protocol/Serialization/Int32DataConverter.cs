namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="int"/> ↔
/// <see cref="DSCode.CacheableInt32"/> (57). Wire payload is 4 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt32</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int32DataConverter : DataConverter<int>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableInt32 };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, int value, byte dsCode, int depth) =>
        writer.WriteInt32(value);

    public override int Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        reader.ReadInt32();
}
