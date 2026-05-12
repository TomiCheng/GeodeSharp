using System.Buffers;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Wire-level helpers for converter tests. Every assertion goes
/// through a freshly-constructed <see cref="SerializationRegistry"/>
/// so the test simultaneously validates the converter's
/// <c>Write</c>/<c>Read</c> bodies, the registry's
/// <c>WriteObject</c>/<c>ReadObject</c> dispatch (including the
/// DSCode byte the registry writes / reads), and the
/// <c>_byType</c>/<c>_byDsCode</c> registration.
/// </summary>
internal static class SerializationTestHelpers
{
    /// <summary>
    /// Encode <paramref name="value"/> through the registry and
    /// return the full wire bytes (DSCode byte + payload).
    /// </summary>
    public static byte[] Encode(object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);
        new SerializationRegistry().WriteObject(writer, value);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Decode wire bytes through the registry. Bytes must start with
    /// a DSCode byte the registry can dispatch on.
    /// </summary>
    public static object? Decode(byte[] bytes)
    {
        var reader = new BigEndianBinaryReader(bytes);
        return new SerializationRegistry().ReadObject(reader);
    }

    /// <summary>
    /// Encode then decode, asserting the value survives the wire.
    /// Caller picks the expected CLR result type via
    /// <typeparamref name="T"/>.
    /// </summary>
    public static T RoundTrip<T>(T value) =>
        (T)Decode(Encode(value!))!;
}
