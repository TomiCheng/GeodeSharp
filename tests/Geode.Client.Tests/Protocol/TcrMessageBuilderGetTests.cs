using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrMessageBuilderGetTests
{
    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder());

    // ====================================================================
    //  Property-level: shape of the resulting TcrMessage
    // ====================================================================

    [Fact]
    public void Get_uses_MessageType_Request()
    {
        var msg = NewBuilder().Get("/test", "k");
        Assert.Equal(MessageType.Request, msg.MessageType);
    }

    [Fact]
    public void Get_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Get("/test", "k");
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Get_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Get("/test", "k", transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Get_zero_EarlyAck_in_phase3()
    {
        var msg = NewBuilder().Get("/test", "k");
        Assert.Equal(0, msg.EarlyAck);
    }

    [Fact]
    public void Get_without_callback_emits_2_parts()
    {
        var msg = NewBuilder().Get("/test", "k");
        Assert.Equal(2, msg.Parts.Count);
    }

    [Fact]
    public void Get_with_callback_emits_3_parts()
    {
        var msg = NewBuilder().Get("/test", "k", callbackArgument: "cb");
        Assert.Equal(3, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ascii_bytes_isObject_zero()
    {
        var msg = NewBuilder().Get("/test", "k");

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_key_string_is_DSCode_tagged_ASCII()
    {
        var msg = NewBuilder().Get("/test", "k");

        var keyPart = msg.Parts[1];
        Assert.Equal((byte)1, keyPart.IsObject);
        // DSCode CacheableASCIIString(87) + u16 len(1) + 'k'(0x6B)
        Assert.Equal(
            new byte[] { DSCode.CacheableASCIIString, 0x00, 0x01, 0x6B },
            keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_callback_is_DSCode_tagged_string()
    {
        var msg = NewBuilder().Get("/test", "k", callbackArgument: "cb");

        var cbPart = msg.Parts[2];
        Assert.Equal((byte)1, cbPart.IsObject);
        // DSCode CacheableASCIIString(87) + u16 len(2) + "cb"
        Assert.Equal(
            new byte[] { DSCode.CacheableASCIIString, 0x00, 0x02, 0x63, 0x62 },
            cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Phase 3 type guards
    // ====================================================================

    [Fact]
    public void Get_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Get(null!, "k"));
    }

    [Fact]
    public void Get_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Get("", "k"));
    }

    [Fact]
    public void Get_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Get("/r", null!));
    }

    [Fact]
    public void Get_throws_for_non_string_key_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Get("/r", 42));
    }

    [Fact]
    public void Get_throws_for_non_string_callback_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Get("/r", "k", callbackArgument: 42));
    }

    // ====================================================================
    //  Encode round-trip — leverages TcrMessage.Decode + record equality
    // ====================================================================

    [Fact]
    public void Get_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Get("/test", "hello");
        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Get_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Get(
            "/test", "k", callbackArgument: "callback-arg", transactionId: 99);
        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }
}
