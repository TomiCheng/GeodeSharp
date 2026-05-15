using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>Query(34)</c> request frame.
/// Mirrors cppcache <c>TcrMessageQuery</c>
/// (<c>cppcache/src/TcrMessage.cpp:1684-1709</c>) used by
/// <c>RemoteQuery::execute</c>.
/// </summary>
public class TcrMessageBuilderQueryTests
{
    private const string Oql = "SELECT * FROM /orders";
    private const long ThreadId = 1L;
    private const long SeqId = 1L;

    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder(), SerializationTestHelpers.CreateRegistry());

    // i32 BE bytes of v.
    private static byte[] Int32Be(int v) =>
    [
        (byte)((v >> 24) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)(v & 0xFF),
    ];

    // ====================================================================
    //  Header shape
    // ====================================================================

    [Fact]
    public void Query_uses_MessageType_Query()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);
        Assert.Equal(MessageType.Query, msg.MessageType);
    }

    [Fact]
    public void Query_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Query_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId, transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Query_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Query_with_default_timeout_emits_3_parts()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);
        Assert.Equal(3, msg.Parts.Count);
    }

    [Fact]
    public void Query_with_null_timeout_omits_timeout_part()
    {
        var msg = NewBuilder().Query(
            Oql, ThreadId, SeqId, messageResponseTimeoutMillis: null);
        Assert.Equal(2, msg.Parts.Count);
    }

    [Fact]
    public void Query_with_explicit_timeout_includes_timeout_part()
    {
        var msg = NewBuilder().Query(
            Oql, ThreadId, SeqId, messageResponseTimeoutMillis: 30_000);
        Assert.Equal(3, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_querystring_is_modified_utf8_bytes_isObject_zero()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);

        var part = msg.Parts[0];
        Assert.Equal((byte)0, part.IsObject);
        // Pure ASCII OQL → modified UTF-8 byte-identical to ASCII.
        Assert.Equal("SELECT * FROM /orders"u8.ToArray(), part.Payload.ToArray());
    }

    [Fact]
    public void Part1_querystring_non_ASCII_encodes_as_modified_utf8()
    {
        // OQL with non-ASCII string literal — modified UTF-8 differs
        // from ASCII (which would drop the chars), proving the encoding
        // upgrade (TcrPartBuilder.ModifiedUtf8) is wired in.
        var oql = "SELECT * FROM /orders WHERE name = '張三'";
        var msg = NewBuilder().Query(oql, ThreadId, SeqId);

        var part = msg.Parts[0];
        Assert.Equal((byte)0, part.IsObject);
        // 張 (U+5F35) → E5 BC B5; 三 (U+4E09) → E4 B8 89 — modified UTF-8.
        var bytes = part.Payload.ToArray();
        // Find the last non-ASCII region near the end (after WHERE name = ')
        Assert.Contains((byte)0xE5, bytes);
        Assert.Contains((byte)0xBC, bytes);
        Assert.Contains((byte)0xB5, bytes);
    }

    [Fact]
    public void Part2_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().Query(
            Oql,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10);

        var part = msg.Parts[1];
        Assert.Equal((byte)0, part.IsObject);
        Assert.Equal(18, part.Payload.Length);
        Assert.Equal(
            new byte[] {
                0x03, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x03, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            },
            part.Payload.ToArray());
    }

    [Fact]
    public void Part3_timeout_is_4_byte_i32_be_isObject_zero()
    {
        var msg = NewBuilder().Query(
            Oql, ThreadId, SeqId, messageResponseTimeoutMillis: 30_000);

        var part = msg.Parts[2];
        Assert.Equal((byte)0, part.IsObject);
        Assert.Equal(Int32Be(30_000), part.Payload.ToArray());
    }

    [Fact]
    public void Default_timeout_part_carries_15000_ms()
    {
        var msg = NewBuilder().Query(Oql, ThreadId, SeqId);

        // Default = cppcache DEFAULT_QUERY_RESPONSE_TIMEOUT = 15 seconds.
        Assert.Equal(Int32Be(15_000), msg.Parts[2].Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void Query_throws_for_null_querystring()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Query(null!, ThreadId, SeqId));
    }

    [Fact]
    public void Query_throws_for_empty_querystring()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Query("", ThreadId, SeqId));
    }

    [Fact]
    public void Query_throws_for_whitespace_querystring()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Query("   ", ThreadId, SeqId));
    }

    // ====================================================================
    //  Encode round-trip
    // ====================================================================

    [Fact]
    public void Query_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Query(Oql, ThreadId, SeqId);
        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Query_with_explicit_timeout_roundtrips()
    {
        var original = NewBuilder().Query(
            Oql, ThreadId, SeqId,
            messageResponseTimeoutMillis: 30_000,
            transactionId: 42);
        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }
}
