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
/// <see cref="DsCodes"/>, <see cref="Write(DataOutput, T, byte)"/>,
/// and <see cref="Read(BigEndianBinaryReader, byte)"/>. They inherit
/// the default <see cref="GetDsCode(T)"/> which returns
/// <c>DsCodes[0]</c> ??fine because their <see cref="DsCodes"/> array
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

    public abstract void Write(DataOutput writer, T value, byte dsCode, int depth);

    public abstract T? Read(BigEndianBinaryReader reader, byte dsCode, int depth);

    /// <summary>
    /// 預設:跑 sync <see cref="Write"/> 然後回 completed task。Recursive
    /// container converter 或將來會 await wire op 的 converter override。
    /// </summary>
    public virtual ValueTask WriteAsync(DataOutput writer, T value, byte dsCode, int depth, CancellationToken ct)
    {
        Write(writer, value, dsCode, depth);
        return ValueTask.CompletedTask;
    }

    /// <summary>預設:跑 sync <see cref="Read"/> 包成 <see cref="ValueTask{TResult}"/>。</summary>
    public virtual ValueTask<T?> ReadAsync(BigEndianBinaryReader reader, byte dsCode, int depth, CancellationToken ct) =>
        ValueTask.FromResult(Read(reader, dsCode, depth));

    // ?�?� Bridges to the non-generic interface ?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�?�
    // The registry calls these overloads, never the typed ones
    // directly. The casts are safe because the registry looks codecs
    // up by ManagedType (encode) / DsCodes (decode). `depth` rides
    // through unchanged ??the registry already does the limit check
    // before calling in; this layer just forwards.
    byte IDataConverter.GetDsCode(object value) =>
        GetDsCode((T)value);

    void IDataConverter.Write(DataOutput writer, object value, byte dsCode, int depth) =>
        Write(writer, (T)value, dsCode, depth);

    object? IDataConverter.Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        Read(reader, dsCode, depth);

    ValueTask IDataConverter.WriteAsync(DataOutput writer, object value, byte dsCode, int depth, CancellationToken ct) =>
        WriteAsync(writer, (T)value, dsCode, depth, ct);

    async ValueTask<object?> IDataConverter.ReadAsync(BigEndianBinaryReader reader, byte dsCode, int depth, CancellationToken ct) =>
        await ReadAsync(reader, dsCode, depth, ct);
}
