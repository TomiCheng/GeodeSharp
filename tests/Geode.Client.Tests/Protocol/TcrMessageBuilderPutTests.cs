using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrMessageBuilderPutTests
{
    private const long ThreadId = 1L;
    private const long SeqId = 1L;

    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder(), new SerializationRegistry());

    // ====================================================================
    //  Property-level: shape of the resulting TcrMessage
    // ====================================================================

    [Fact]
    public void Put_uses_MessageType_Put()
    {
        var msg = NewBuilder().Put(
            regionName: "/test",
            key: "k",
            value: new byte[] { 0x76 },
            callbackArgument: null,
            eventThreadId: ThreadId,
            eventSequenceId: SeqId);

        Assert.Equal(MessageType.Put, msg.MessageType);
    }

    [Fact]
    public void Put_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Put_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId,
            transactionId: 42);

        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Put_zero_EarlyAck_in_phase3()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        Assert.Equal(0, msg.EarlyAck);
    }

    [Fact]
    public void Put_without_callback_emits_7_parts()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        Assert.Equal(7, msg.Parts.Count);
    }

    [Fact]
    public void Put_with_callback_emits_8_parts()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 },
            callbackArgument: "cb", eventThreadId: ThreadId, eventSequenceId: SeqId);

        Assert.Equal(8, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ascii_bytes_isObject_zero()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_operation_is_NullObj_DSCode()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        var opPart = msg.Parts[1];
        Assert.Equal((byte)1, opPart.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, opPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_flags_is_i32_zero_isObject_zero()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        var flagsPart = msg.Parts[2];
        Assert.Equal((byte)0, flagsPart.IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, flagsPart.Payload.ToArray());
    }

    [Fact]
    public void Part4_key_string_is_DSCode_tagged_ASCII()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        var keyPart = msg.Parts[3];
        Assert.Equal((byte)1, keyPart.IsObject);
        // DSCode CacheableASCIIString(87) + u16 len(1) + 'k'(0x6B)
        Assert.Equal(
            new byte[] { DSCode.CacheableASCIIString, 0x00, 0x01, 0x6B },
            keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part5_isDelta_false_is_CacheableBoolean_zero()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId);

        var isDeltaPart = msg.Parts[4];
        Assert.Equal((byte)1, isDeltaPart.IsObject);
        Assert.Equal(
            new byte[] { DSCode.CacheableBoolean, 0x00 },
            isDeltaPart.Payload.ToArray());
    }

    [Fact]
    public void Part5_isDelta_true_is_CacheableBoolean_one()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null, ThreadId, SeqId,
            isDelta: true);

        var isDeltaPart = msg.Parts[4];
        Assert.Equal(
            new byte[] { DSCode.CacheableBoolean, 0x01 },
            isDeltaPart.Payload.ToArray());
    }

    [Fact]
    public void Part6_value_is_raw_bytes_isObject_zero_no_dscode()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03 };
        var msg = NewBuilder().Put(
            "/test", "k", bytes, null, ThreadId, SeqId);

        var valuePart = msg.Parts[5];
        Assert.Equal((byte)0, valuePart.IsObject);
        Assert.Equal(bytes, valuePart.Payload.ToArray());
    }

    [Fact]
    public void Part7_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 }, null,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10);

        var eventIdPart = msg.Parts[6];
        Assert.Equal((byte)0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
        // [longCode=3][i64 BE threadId][longCode=3][i64 BE seqId]
        Assert.Equal(
            new byte[] {
                0x03, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x03, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            },
            eventIdPart.Payload.ToArray());
    }

    [Fact]
    public void Part8_callback_is_DSCode_tagged_string()
    {
        var msg = NewBuilder().Put(
            "/test", "k", new byte[] { 0x76 },
            callbackArgument: "cb", eventThreadId: ThreadId, eventSequenceId: SeqId);

        var cbPart = msg.Parts[7];
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
    public void Put_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Put(null!, "k", new byte[] { 0x01 }, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Put("", "k", new byte[] { 0x01 }, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Put("/r", null!, new byte[] { 0x01 }, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_non_string_key_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", 42, new byte[] { 0x01 }, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_null_value_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", "k", null, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_non_byteArray_value_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", "k", "string-value", null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_empty_byteArray_value_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", "k", Array.Empty<byte>(), null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_non_string_callback_in_phase3()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", "k", new byte[] { 0x01 },
                callbackArgument: 42, eventThreadId: ThreadId, eventSequenceId: SeqId));
    }

    // ====================================================================
    //  Encode round-trip — leverages TcrMessage.Decode + record equality
    // ====================================================================

    [Fact]
    public void Put_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Put(
            "/test", "hello", new byte[] { 0x77, 0x6F, 0x72, 0x6C, 0x64 }, null,
            eventThreadId: 1L, eventSequenceId: 1L);

        var bytes = original.Encode();
        var decoded = TcrMessage.Decode(bytes);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Put_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Put(
            "/test", "k", new byte[] { 0x01 },
            callbackArgument: "callback-arg",
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10,
            transactionId: 42,
            isDelta: true);

        var decoded = TcrMessage.Decode(original.Encode());

        Assert.Equal(original, decoded);
    }
}
