namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="long"/> ↔
/// <see cref="DSCode.CacheableInt64"/> (58). Wire payload is 8 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt64</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int64DataConverter : DataConverter<long>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableInt64 };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(BigEndianBinaryWriter writer, long value, byte dsCode) =>
        writer.WriteInt64(value);

    public override long Read(BigEndianBinaryReader reader, byte dsCode) =>
        reader.ReadInt64();
}
