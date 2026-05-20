namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// <see cref="IDataConverter"/> for <see cref="string"/><c>[]</c> ??/// <see cref="DSCode.CacheableStringArray"/> (64). Wire payload is a
/// VL-encoded length (1 / 3 / 5 bytes) followed by N
/// <i>fully-serialised objects</i> ??each element starts with its
/// own DSCode byte (42 / 87 / 88 / 89 for the four string variants,
/// or 41 for <c>null</c> elements). Mirrors cppcache
/// <c>CacheableStringArray</c>
/// (<c>CacheableArrayPrimitive&lt;shared_ptr&lt;CacheableString&gt;,
/// CacheableStringArray&gt;</c>) which routes through
/// <c>serializer::writeArrayObject</c> ??<c>writeObject(shared_ptr)</c>
/// per element (the <c>shared_ptr</c> overload writes DSCode +
/// payload via the registry, NOT a raw string body).
/// </summary>
/// <remarks>
/// <para>
/// <b>Different from the primitive array converters.</b> The
/// <c>bool[]</c> / <c>int[]</c> / ??paths write raw element bytes
/// with no per-element DSCode (the array's DSCode 26 / 48 / ??fully
/// specifies the element shape). For <see cref="string"/><c>[]</c>
/// the per-element shape is ambiguous (ASCII short vs modified-UTF-8
/// vs UTF-16 huge), so cppcache + Java write the full DSCode +
/// payload per element. We delegate to
/// <see cref="SerializationRegistry.WriteObject"/> /
/// <see cref="SerializationRegistry.ReadObject"/> so the choice
/// matches <see cref="StringDataConverter"/> exactly element by
/// element.
/// </para>
/// <para>
/// <b>Why the registry reference</b>: writing one element needs the
/// same encode dispatch that a top-level <c>Put</c> uses ??pick a
/// DSCode (42 / 87 / 88 / 89), emit it, write the body. Reading
/// needs the symmetric path. Passing the registry through the
/// constructor keeps this converter unaware of <c>StringDataConverter</c>
/// directly and makes adding non-ASCII / huge elements transparent.
/// </para>
/// <para>
/// <c>null</c> elements survive the round trip: writer hits the
/// registry's <c>WriteObject(value: null)</c> branch and emits DSCode
/// 41 (<see cref="DSCode.NullObj"/>); reader sees 41 and returns
/// <c>null</c> into the result array slot. A top-level <c>null</c>
/// <see cref="string"/><c>[]</c> (the whole array is null) is
/// intercepted by the registry one level higher and never reaches
/// this converter.
/// </para>
/// </remarks>
internal sealed class StringArrayDataConverter : DataConverter<string[]>
{
    private static readonly byte[] s_dsCodes = { DSCode.CacheableStringArray };

    private readonly SerializationRegistry _registry;

    /// <summary>
    /// Takes the owning <see cref="SerializationRegistry"/> so each
    /// element can re-enter <see cref="SerializationRegistry.WriteObject"/>
    /// /<see cref="SerializationRegistry.ReadObject"/>. The
    /// <c>this</c>-reference at registry-construction time is safe:
    /// we only store it and call it later from <see cref="Write"/> /
    /// <see cref="Read"/>, by which point the registry is fully
    /// populated.
    /// </summary>
    public StringArrayDataConverter(SerializationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public override byte[] DsCodes => s_dsCodes;

    public override void Write(DataOutput writer, string[] value, byte dsCode, int depth)
    {
        if (value.Length > _registry.MaxArrayLength)
        {
            throw new InvalidOperationException(
                $"StringArrayDataConverter: cannot serialise an array of {value.Length} elements "
                + $"??exceeds Serialization.MaxArrayLength ({_registry.MaxArrayLength}).");
        }
        writer.WriteArrayLen(value.Length);
        foreach (var element in value)
        {
            // WriteObject handles null ??DSCode.NullObj (41) and
            // picks the correct string DSCode (42 / 87 / 88 / 89)
            // for non-null elements. depth + 1 propagates the
            // recursion budget into the registry ??even leaf strings
            // count, keeping the limit symmetric with container
            // elements.
            _registry.WriteObject(writer, element, depth + 1);
        }
    }

    public override string[] Read(BigEndianBinaryReader reader, byte dsCode, int depth)
    {
        var length = reader.ReadArrayLen();
        if (length <= 0)
        {
            return Array.Empty<string>();
        }
        if (length > _registry.MaxArrayLength)
        {
            throw new GeodeException(
                $"StringArrayDataConverter: wire array length {length} exceeds "
                + $"Serialization.MaxArrayLength ({_registry.MaxArrayLength}) ??refusing to allocate.");
        }
        // Element type is string?[] in spirit (nulls survive), but the
        // CLR Type is the same string[] either way ??nullable
        // annotations aren't part of runtime type identity, so the
        // registry's _byType lookup hits this converter for both
        // string[] and string?[] uses on the consumer side.
        var array = new string[length];
        for (var i = 0; i < length; i++)
        {
            // Cast is safe: the wire DSCode dispatch on the read
            // side will either return a string (from StringDataConverter)
            // or null (NullObj=41 handled by the registry). Anything
            // else means corrupt wire ??let InvalidCastException
            // surface that as a hard fault rather than silently
            // produce wrong data.
            array[i] = (string)_registry.ReadObject(reader, depth + 1)!;
        }
        return array;
    }
}
