namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="double"/> ??/// <see cref="DSCode.CacheableDouble"/> (60). Wire payload is 8 bytes
/// IEEE-754 big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableDouble</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
/// <remarks>
/// NaN / ±??wire shapes are JVM-identical (Java
/// <c>Double.doubleToRawLongBits</c> ??.NET
/// <see cref="System.BitConverter.DoubleToInt64Bits"/>). Same key
/// caveats as <see cref="SingleDataConverter"/>: legal but
/// impractical.
/// </remarks>
internal sealed class DoubleDataConverter : DataConverter<double>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableDouble };

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(DataOutput writer, double value, byte dsCode, int depth) =>
        writer.WriteDouble(value);

    public override double Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        reader.ReadDouble();
}
