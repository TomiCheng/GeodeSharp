namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="int"/> ??/// <see cref="DSCode.CacheableInt32"/> (57). Wire payload is 4 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt32</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int32DataConverter : DataConverter<int>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableInt32 };

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, int value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteInt32(value);
        return ValueTask.CompletedTask;
    }

    public override int Read(DataInput reader, byte dsCode, int depth) =>
        reader.ReadInt32();
}
