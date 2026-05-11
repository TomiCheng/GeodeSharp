namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Default base for built-in codecs. Bridges the typed
/// <see cref="IDataConverter{T}"/> contract to the erased
/// <see cref="IDataConverter"/> one used by the registry, so concrete
/// codecs only override <see cref="Write(BigEndianBinaryWriter, T)"/>
/// + <see cref="Read(BigEndianBinaryReader)"/>.
/// </summary>
/// <typeparam name="T">CLR type the codec serialises.</typeparam>
internal abstract class DataConverter<T> : IDataConverter<T>
{
    public abstract byte DsCode { get; }

    public Type ManagedType => typeof(T);

    public abstract void Write(BigEndianBinaryWriter writer, T value);

    public abstract T? Read(BigEndianBinaryReader reader);

    // Bridge to the non-generic interface — the registry calls these
    // overloads, never the typed ones directly. The cast in Write is
    // safe because the registry looks codecs up by ManagedType.
    void IDataConverter.Write(BigEndianBinaryWriter writer, object value) =>
        Write(writer, (T)value);

    object? IDataConverter.Read(BigEndianBinaryReader reader) =>
        Read(reader);
}
