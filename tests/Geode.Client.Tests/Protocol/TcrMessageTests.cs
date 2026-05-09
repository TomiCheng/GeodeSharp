using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrMessageTests
{
    // ====================================================================
    //  Round-trip tests
    // ====================================================================

    [Fact]
    public void Round_trip_Ping_with_no_parts()
    {
        var original = new TcrMessage(
            MessageType: MessageType.Ping,
            TransactionId: 42,
            EarlyAck: 0,
            Parts: Array.Empty<TcrPart>());

        var bytes = original.Encode();
        var decoded = TcrMessage.Decode(bytes);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Round_trip_Put_with_one_byte_part()
    {
        var original = new TcrMessage(
            MessageType: MessageType.Put,
            TransactionId: 99,
            EarlyAck: 0,
            Parts: new[]
            {
                new TcrPart(IsObject: 0, Payload: new byte[] { 0xAB }),
            });

        var bytes = original.Encode();
        var decoded = TcrMessage.Decode(bytes);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Round_trip_with_multiple_mixed_parts()
    {
        var original = new TcrMessage(
            MessageType: MessageType.Query,
            TransactionId: 1234,
            EarlyAck: 0x02,
            Parts: new[]
            {
                new TcrPart(IsObject: 0, Payload: new byte[] { 0x01, 0x02 }),
                new TcrPart(IsObject: 1,  Payload: new byte[] { 0x57, 0x05, 0xAA, 0xBB }),
                new TcrPart(IsObject: 0, Payload: ReadOnlyMemory<byte>.Empty),
            });

        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }

    // ====================================================================
    //  Byte-fixture tests
    //
    //  Fixtures are derived from cppcache wire format:
    //    - Header layout: TcrMessage::writeHeader  (TcrMessage.cpp line 767)
    //    - MessageLength = totalBytes - kHeaderLength
    //                     (TcrMessage::writeMessageLength, line 844-854)
    //    - Part layout:   writeBytePart, writeIntPart, ... (line 328-)
    //                     each writes  i32 length | u8 isObject | payload
    //    - Header is 17 bytes (4×i32 + 1×u8); EarlyAck sits at offset 16
    //                     (line 794-798).
    // ====================================================================

    /// <summary>
    /// Ping (msgType=5), no parts, txId=42, earlyAck=0.
    ///
    /// 17 bytes:
    ///   00 00 00 05  | i32 MessageType    = 5 (Ping)
    ///   00 00 00 00  | i32 MessageLength  = 0 (no parts)
    ///   00 00 00 00  | i32 NumParts       = 0
    ///   00 00 00 2A  | i32 TransactionId  = 42
    ///   00           | u8  EarlyAck       = 0
    /// </summary>
    private static readonly byte[] PingFixture =
    {
        0x00, 0x00, 0x00, 0x05,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x2A,
        0x00,
    };

    /// <summary>
    /// Put (msgType=7), txId=99, earlyAck=0, one part with payload [0xAB].
    ///
    /// 23 bytes (17 header + 6 part):
    ///   00 00 00 07  | i32 MessageType    = 7 (Put)
    ///   00 00 00 06  | i32 MessageLength  = 6 (one part: 4+1+1)
    ///   00 00 00 01  | i32 NumParts       = 1
    ///   00 00 00 63  | i32 TransactionId  = 99
    ///   00           | u8  EarlyAck       = 0
    ///   00 00 00 01  | i32 Part0.PartLength = 1
    ///   00           | u8  Part0.IsObject  = false
    ///   AB           | u8  Part0.payload[0]
    /// </summary>
    private static readonly byte[] PutWithBytePartFixture =
    {
        0x00, 0x00, 0x00, 0x07,
        0x00, 0x00, 0x00, 0x06,
        0x00, 0x00, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x63,
        0x00,
        0x00, 0x00, 0x00, 0x01, 0x00, 0xAB,
    };

    [Fact]
    public void Encode_Ping_produces_expected_byte_fixture()
    {
        var msg = new TcrMessage(
            MessageType: MessageType.Ping,
            TransactionId: 42,
            EarlyAck: 0,
            Parts: Array.Empty<TcrPart>());

        Assert.Equal(PingFixture, msg.Encode());
    }

    [Fact]
    public void Decode_Ping_byte_fixture_reproduces_message()
    {
        var decoded = TcrMessage.Decode(PingFixture);

        Assert.Equal(MessageType.Ping, decoded.MessageType);
        Assert.Equal(42, decoded.TransactionId);
        Assert.Equal(0, decoded.EarlyAck);
        Assert.Empty(decoded.Parts);
    }

    [Fact]
    public void Encode_Put_with_byte_part_produces_expected_byte_fixture()
    {
        var msg = new TcrMessage(
            MessageType: MessageType.Put,
            TransactionId: 99,
            EarlyAck: 0,
            Parts: new[]
            {
                new TcrPart(IsObject: 0, Payload: new byte[] { 0xAB }),
            });

        Assert.Equal(PutWithBytePartFixture, msg.Encode());
    }

    [Fact]
    public void Decode_Put_byte_fixture_reproduces_message()
    {
        var decoded = TcrMessage.Decode(PutWithBytePartFixture);

        Assert.Equal(MessageType.Put, decoded.MessageType);
        Assert.Equal(99, decoded.TransactionId);
        Assert.Single(decoded.Parts);
        Assert.Equal((byte)0, decoded.Parts[0].IsObject);
        Assert.Equal(new byte[] { 0xAB }, decoded.Parts[0].Payload.ToArray());
    }

    // ====================================================================
    //  Validation tests
    // ====================================================================

    [Fact]
    public void Decode_negative_NumParts_throws_FormatException()
    {
        var bytes = new byte[]
        {
            0x00, 0x00, 0x00, 0x05,
            0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0xFF, 0xFF,   // NumParts = -1
            0x00, 0x00, 0x00, 0x00,
            0x00,
        };
        Assert.Throws<FormatException>(() => TcrMessage.Decode(bytes));
    }

    [Fact]
    public void Decode_MessageLength_disagreeing_with_actual_parts_throws()
    {
        // Header claims MessageLength=10 but the single part is only 5 bytes
        // (4 length + 1 isObject + 0 payload).
        var bytes = new byte[]
        {
            0x00, 0x00, 0x00, 0x05,
            0x00, 0x00, 0x00, 0x0A,   // MessageLength = 10 (wrong)
            0x00, 0x00, 0x00, 0x01,   // NumParts = 1
            0x00, 0x00, 0x00, 0x00,
            0x00,
            0x00, 0x00, 0x00, 0x00,   // Part: length=0
            0x00,                      // isObject=false
        };
        var ex = Assert.Throws<FormatException>(() => TcrMessage.Decode(bytes));
        Assert.Contains("MessageLength", ex.Message);
    }

    [Fact]
    public void Equality_compares_parts_element_wise()
    {
        var a = new TcrMessage(MessageType.Put, 1, 0, new[]
        {
            new TcrPart(0, new byte[] { 0xAA }),
        });
        var b = new TcrMessage(MessageType.Put, 1, 0, new[]
        {
            new TcrPart(0, new byte[] { 0xAA }),
        });

        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }
}
