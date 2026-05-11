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
    /// <see cref="IDataConverter.Write(BigEndianBinaryWriter, object)"/>;
    /// no boxing.
    /// </summary>
    void Write(BigEndianBinaryWriter writer, T value);

    /// <summary>
    /// Typed counterpart to
    /// <see cref="IDataConverter.Read(BigEndianBinaryReader)"/>; no
    /// boxing.
    /// </summary>
    new T? Read(BigEndianBinaryReader reader);
}
