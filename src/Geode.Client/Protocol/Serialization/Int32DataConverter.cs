namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="int"/> ↔
/// <see cref="DSCode.CacheableInt32"/> (57). Wire payload is 4 bytes
/// big-endian, no length prefix. Mirrors cppcache
/// <c>CacheableInt32</c> (<c>cppcache/src/CacheableBuiltins.cpp</c>
/// <c>toData</c> / <c>fromData</c>).
/// </summary>
internal sealed class Int32DataConverter : IDataConverter
{
    public byte DsCode => DSCode.CacheableInt32;

    public Type ManagedType => typeof(int);

    public void Write(BigEndianBinaryWriter writer, object value) =>
        writer.WriteInt32((int)value);

    public object? Read(BigEndianBinaryReader reader) =>
        reader.ReadInt32();
}
