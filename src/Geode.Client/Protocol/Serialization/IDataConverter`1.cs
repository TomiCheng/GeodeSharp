namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Strongly-typed variant of <see cref="IDataConverter"/>. Concrete
/// codecs (<see cref="DataConverter{T}"/> derivatives) implement this
/// so the <see cref="object"/> boxing only happens at the
/// registry boundary, not inside the codec itself.
/// </summary>
/// <typeparam name="T">CLR type the codec serialises.</typeparam>
internal interface IDataConverter<T> : IDataConverter
{
    /// <summary>
    /// Typed counterpart to
    /// <see cref="IDataConverter.GetDsCode(object)"/>; no boxing.
    /// </summary>
    byte GetDsCode(T value);

    /// <summary>
    /// Typed counterpart to
    /// <see cref="IDataConverter.Write(DataOutput, object, byte, int)"/>;
    /// no boxing.
    /// </summary>
    void Write(DataOutput writer, T value, byte dsCode, int depth);

    /// <summary>
    /// Typed counterpart to
    /// <see cref="IDataConverter.Read(BigEndianBinaryReader, byte, int)"/>;
    /// no boxing.
    /// </summary>
    new T? Read(BigEndianBinaryReader reader, byte dsCode, int depth);
}
