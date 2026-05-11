namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="bool"/> ↔
/// <see cref="DSCode.CacheableBoolean"/> (53). Wire payload is 1
/// byte: <c>0</c> = false, non-zero = true. Mirrors cppcache
/// <c>CacheableBoolean</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class BooleanDataConverter : IDataConverter
{
    public byte DsCode => DSCode.CacheableBoolean;

    public Type ManagedType => typeof(bool);

    public void Write(BigEndianBinaryWriter writer, object value) =>
        writer.WriteByte((bool)value ? (byte)1 : (byte)0);

    public object? Read(BigEndianBinaryReader reader) =>
        reader.ReadByte() != 0;
}
