namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="float"/> ??/// <see cref="DSCode.CacheableFloat"/> (59). Wire payload is 4 bytes
/// IEEE-754 big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableFloat</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
/// <remarks>
/// NaN / ±??wire shapes are JVM-identical (Java
/// <c>Float.floatToRawIntBits</c> ??.NET
/// <see cref="System.BitConverter.SingleToInt32Bits"/>), so no
/// special handling needed for those payloads. Using <c>float</c> as
/// a region <b>Key</b> compiles (it implements
/// <see cref="IEquatable{T}"/>) but is impractical: <c>NaN</c> keys
/// can never be found again (NaN ??NaN under IEEE-754) and ±0
/// collide. Use integral keys when possible.
/// </remarks>
internal sealed class SingleDataConverter : DataConverter<float>
{
    private static readonly byte[] _dsCodes = { DSCode.CacheableFloat };

    public override byte[] DsCodes => _dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, float value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteFloat(value);
        return ValueTask.CompletedTask;
    }

    public override float Read(DataInput reader, byte dsCode, int depth) =>
        reader.ReadFloat();
}
