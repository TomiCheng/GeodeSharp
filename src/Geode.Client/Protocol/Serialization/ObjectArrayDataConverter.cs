namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="object"/><c>[]</c> ??/// <see cref="DSCode.CacheableObjectArray"/> (52). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by a Java class header
/// (one <see cref="DSCode.Class"/> tag byte + the string
/// <c>"java.lang.Object"</c> via the standard string-write path)
/// followed by N <i>fully-serialised objects</i> ??each element starts
/// with its own DSCode byte (including <see cref="DSCode.NullObj"/> for
/// null elements). Mirrors cppcache <c>CacheableObjectArray</c>
/// (<c>cppcache/src/CacheableObjectArray.cpp</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The class-name header is part of the wire format, not metadata
/// we can drop.</b> Java's <c>DataSerializer</c> writes an Object[]
/// as <c>arrayLength ??componentTypeName ??elements</c>. We write
/// the fixed string <c>"java.lang.Object"</c> (matching cppcache ??/// we don't preserve the .NET runtime element type) and on read we
/// consume the bytes without using them: the wire dictates the
/// element type sequence per-element via each element's DSCode, so
/// the header is informational only on this side.
/// </para>
/// <para>
/// <b>Why a registry reference</b>: each element re-enters
/// <see cref="SerializationRegistry.WriteObject"/> /
/// <see cref="SerializationRegistry.ReadObject"/> so any registered
/// type can appear in a slot (string, int, bool, even nested arrays).
/// Same pattern as <see cref="StringArrayDataConverter"/>. Passing
/// the registry through the constructor keeps the converter free of
/// direct knowledge about which converters handle which element
/// type.
/// </para>
/// <para>
/// <b><c>null</c> elements</b> survive the round trip: the registry
/// emits <see cref="DSCode.NullObj"/> for any null value passed to
/// <see cref="SerializationRegistry.WriteObject"/>, and decodes
/// DSCode 41 back to <c>null</c>. A top-level <c>null</c>
/// <see cref="object"/><c>[]</c> (the array itself being null) is
/// intercepted by the registry one level higher and never reaches
/// this converter.
/// </para>
/// <para>
/// <b>Type identity:</b> <c>typeof(object[])</c> only matches values
/// whose runtime type is exactly <c>object[]</c>. A <c>string[]</c>
/// or <c>int[]</c> stored in an <c>object</c> variable is still
/// <c>string[]</c> / <c>int[]</c> at <see cref="object.GetType"/>
/// time, so they dispatch to <see cref="StringArrayDataConverter"/>
/// / <see cref="Int32ArrayDataConverter"/> respectively ??not here.
/// To force polymorphic element types on the wire, the caller must
/// explicitly allocate <c>new object[] { ... }</c>.
/// </para>
/// </remarks>
internal sealed class ObjectArrayDataConverter : DataConverter<object[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableObjectArray };

    private const string JavaObjectClassName = "java.lang.Object";

    private readonly SerializationRegistry _registry;

    /// <summary>
    /// Takes the owning <see cref="SerializationRegistry"/> so each
    /// element can re-enter <see cref="SerializationRegistry.WriteObject"/>
    /// /<see cref="SerializationRegistry.ReadObject"/>. The
    /// <c>this</c>-reference at registry-construction time is safe
    /// for the same reason as
    /// <see cref="StringArrayDataConverter"/> ??we only store the
    /// reference and call it later from <see cref="Write"/> /
    /// <see cref="Read"/>, by which point the registry is fully
    /// populated.
    /// </summary>
    public ObjectArrayDataConverter(SerializationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public override byte[] DsCodes => s_dsCodes;

    public override async ValueTask WriteAsync(DataOutput writer, object[] value, byte dsCode, int depth, CancellationToken ct)
    {
        if (value.Length > _registry.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"ObjectArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"— exceeds Serialization.MaxArrayLength ({_registry.MaxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        writer.WriteByte(DSCode.Class);
        writer.WriteString(JavaObjectClassName);

        foreach (var element in value)
        {
            await _registry.WriteObjectAsync(writer, element, depth + 1, ct);
        }
    }

    public override object[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<object>();
        }
        if (length > _registry.MaxArrayLength)
        {
            throw new GeodeException(
                $"ObjectArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_registry.MaxArrayLength}) ??refusing to allocate.");
        }

        // Discard the class header ??its information is redundant
        // with the per-element DSCode bytes that follow. cppcache's
        // fromData reads + ignores these too.
        //   reader.ReadByte()           ??DSCode.Class tag
        //   _registry.ReadObject()      ??the "java.lang.Object" string,
        //                                 routed via StringDataConverter
        reader.ReadByte();
        _registry.ReadObject(reader, depth + 1);

        var array = new object[length];
        for (var i = 0; i < length; i++)
        {
            // Element slot is object ??any registered type (including
            // null via DSCode.NullObj) is a valid value. The "!" is a
            // CS8601 dance: the slot's static type is non-nullable
            // object, but at runtime CLR arrays of reference types
            // accept null in any slot. Tested in
            // ObjectArrayDataConverterTests.RoundTrip_with_null_elements.
            array[i] = _registry.ReadObject(reader, depth + 1)!;
        }
        return array;
    }
}
