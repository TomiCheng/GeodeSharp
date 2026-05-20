using System.Buffers;
using Geode.Client.Protocol;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrPartTests
{
    [Fact]
    public void Round_trip_with_simple_payload()
    {
        var original = new TcrPart(IsObject: 0, Payload: new byte[] { 0xDE, 0xAD });

        using var w = new DataOutput(SerializationTestHelpers.CreateRegistry());
        original.Encode(w);
        var decoded = TcrPart.Decode(new BigEndianBinaryReader(w.WrittenSpan.ToArray()));

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Round_trip_with_empty_payload()
    {
        var original = new TcrPart(IsObject: 0, Payload: ReadOnlyMemory<byte>.Empty);

        using var w = new DataOutput(SerializationTestHelpers.CreateRegistry());
        original.Encode(w);
        // Encoded bytes: 4 (length=0) + 1 (isObject=0) = 5 bytes.
        Assert.Equal(5, w.WrittenSpan.ToArray().Length);

        var decoded = TcrPart.Decode(new BigEndianBinaryReader(w.WrittenSpan.ToArray()));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Round_trip_with_isObject_true()
    {
        var original = new TcrPart(IsObject: 1, Payload: new byte[] { 0x57 /* DSCode for String */, 0x42 });

        using var w = new DataOutput(SerializationTestHelpers.CreateRegistry());
        original.Encode(w);
        var decoded = TcrPart.Decode(new BigEndianBinaryReader(w.WrittenSpan.ToArray()));

        Assert.Equal((byte)1, decoded.IsObject);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_negative_length_throws_FormatException()
    {
        // PartLength = -1 (0xFFFFFFFF) is invalid.
        var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };
        Assert.Throws<FormatException>(
            () => TcrPart.Decode(new BigEndianBinaryReader(bytes)));
    }

    [Fact]
    public void Decode_truncated_buffer_throws_EndOfStreamException()
    {
        // Says PartLength = 10 but only 5 bytes follow the header.
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x0A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.Throws<EndOfStreamException>(
            () => TcrPart.Decode(new BigEndianBinaryReader(bytes)));
    }

    [Fact]
    public void Equality_is_content_based_not_reference_based()
    {
        // Two parts with identical content but distinct backing arrays must compare equal.
        var a = new TcrPart(IsObject: 0, Payload: new byte[] { 0x01, 0x02, 0x03 });
        var b = new TcrPart(IsObject: 0, Payload: new byte[] { 0x01, 0x02, 0x03 });

        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void Different_payload_compares_not_equal()
    {
        var a = new TcrPart(IsObject: 0, Payload: new byte[] { 0x01 });
        var b = new TcrPart(IsObject: 0, Payload: new byte[] { 0x02 });

        Assert.NotEqual(b, a);
    }
}
