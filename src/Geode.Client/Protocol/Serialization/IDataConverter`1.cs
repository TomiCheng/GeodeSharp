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

    /// <summary>Typed async 版,no boxing。</summary>
    ValueTask WriteAsync(DataOutput writer, T value, byte dsCode, int depth, CancellationToken ct);

    /// <summary>
    /// Typed counterpart to
    /// <see cref="IDataConverter.Read(DataInput, byte, int)"/>;
    /// no boxing.
    /// </summary>
    new T? Read(DataInput reader, byte dsCode, int depth);

    /// <summary>Typed async 版,no boxing。</summary>
    new ValueTask<T?> ReadAsync(DataInput reader, byte dsCode, int depth, CancellationToken ct);

    /// <summary>
    /// Typed counterpart to <see cref="IDataConverter.GetObjectSize"/>;
    /// no boxing。實作者只需 override 這個 typed 版本,non-generic 由
    /// <see cref="DataConverter{T}"/> 自動 bridge。
    /// </summary>
    int GetObjectSize(T value);
}
