/*
namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="short"/> ??/// <see cref="DSCode.CacheableInt16"/> (56). Wire payload is 2 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt16</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int16DataConverter : DataConverter<short>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableInt16 };

    public override byte[] DsCodes => s_dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, short value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteInt16(value);
        return ValueTask.CompletedTask;
    }

    public override short Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        reader.ReadInt16();
}

*/