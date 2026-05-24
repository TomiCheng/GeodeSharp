namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="long"/> ??/// <see cref="DSCode.CacheableInt64"/> (58). Wire payload is 8 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt64</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int64DataConverter : DataConverter<long>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableInt64 };

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, long value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteInt64(value);
        return ValueTask.CompletedTask;
    }

    public override long Read(DataInput reader, byte dsCode, int depth) =>
        reader.ReadInt64();
}
