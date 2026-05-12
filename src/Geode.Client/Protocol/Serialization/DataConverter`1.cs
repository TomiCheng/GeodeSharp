namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Default base for built-in codecs. Bridges the typed
/// <see cref="IDataConverter{T}"/> contract to the erased
/// <see cref="IDataConverter"/> one used by the registry, so concrete
/// codecs only override the typed methods.
/// </summary>
/// <typeparam name="T">CLR type the codec serialises.</typeparam>
/// <remarks>
/// Single-DSCode converters (the common case) only override
/// <see cref="DsCodes"/>, <see cref="Write(BigEndianBinaryWriter, T, byte)"/>,
/// and <see cref="Read(BigEndianBinaryReader, byte)"/>. They inherit
/// the default <see cref="GetDsCode(T)"/> which returns
/// <c>DsCodes[0]</c> — fine because their <see cref="DsCodes"/> array
/// is one element long. Multi-DSCode converters (only
/// <c>StringDataConverter</c> today) override <see cref="GetDsCode(T)"/>
/// to scan the value and branch.
/// </remarks>
internal abstract class DataConverter<T> : IDataConverter<T>
{
    public abstract byte[] DsCodes { get; }

    public Type ManagedType => typeof(T);

    /// <summary>
    /// Default: emit the first (and usually only) DSCode this
    /// converter handles. Multi-DSCode converters override.
    /// </summary>
    public virtual byte GetDsCode(T value) => DsCodes[0];

    public abstract void Write(BigEndianBinaryWriter writer, T value, byte dsCode);

    public abstract T? Read(BigEndianBinaryReader reader, byte dsCode);

    // ── Bridges to the non-generic interface ──────────────────────
    // The registry calls these overloads, never the typed ones
    // directly. The casts are safe because the registry looks codecs
    // up by ManagedType (encode) / DsCodes (decode).
    byte IDataConverter.GetDsCode(object value) =>
        GetDsCode((T)value);

    void IDataConverter.Write(BigEndianBinaryWriter writer, object value, byte dsCode) =>
        Write(writer, (T)value, dsCode);

    object? IDataConverter.Read(BigEndianBinaryReader reader, byte dsCode) =>
        Read(reader, dsCode);
}
