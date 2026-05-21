namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="byte"/> ??/// <see cref="DSCode.CacheableByte"/> (55). Wire payload is 1 byte.
/// Mirrors cppcache <c>CacheableByte</c>
/// (<c>cppcache/src/CacheableBuiltins.cpp</c> <c>toData</c> /
/// <c>fromData</c>).
/// </summary>
/// <remarks>
/// <b>Signed vs unsigned</b>: cppcache / Java treat
/// <c>CacheableByte</c> as <c>int8_t</c> / signed Java <c>byte</c>
/// (range -128..127). We expose it as .NET <see cref="byte"/>
/// (unsigned 0..255) ??the wire bit pattern is identical
/// (.NET <c>255</c> ??Java <c>-1</c> ??wire <c>0xFF</c>) so
/// interop is correct; only the cross-language debug display
/// differs. Choosing <c>byte</c> over <see cref="sbyte"/> matches
/// .NET convention and keeps the type symmetrical with
/// <see cref="DSCode.CacheableBytes"/> (<see cref="byte"/><c>[]</c>).
/// </remarks>
internal sealed class ByteDataConverter : DataConverter<byte>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableByte };

    public override byte[] DsCodes => s_dsCodes;

    public override ValueTask WriteAsync(DataOutput writer, byte value, byte dsCode, int depth, CancellationToken ct)
    {
        writer.WriteByte(value);
        return ValueTask.CompletedTask;
    }

    public override byte Read(BigEndianBinaryReader reader, byte dsCode, int depth) =>
        reader.ReadByte();
}
