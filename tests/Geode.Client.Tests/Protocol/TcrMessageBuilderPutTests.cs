using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Phase 1.2 walking-skeleton scope: int32 keys + int32 values (the
/// registry ships <c>Int32DataConverter</c> + <c>BooleanDataConverter</c>).
/// String / byte[] / Date / collection coverage lands as their
/// converters do.
/// </summary>
public class TcrMessageBuilderPutTests
{
    private const int Key = 123;
    private const int Value = 456;
    private const long ThreadId = 1L;
    private const long SeqId = 1L;

    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder(), SerializationTestHelpers.CreateRegistry());

    private static byte[] EncodedInt32(int v) =>
    [
        DSCode.CacheableInt32,
        (byte)((v >> 24) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)(v & 0xFF),
    ];

    // ====================================================================
    //  Header shape
    // ====================================================================

    [Fact]
    public void Put_uses_MessageType_Put()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, callbackArgument: null,
            ThreadId, SeqId);

        Assert.Equal(MessageType.Put, msg.MessageType);
    }

    [Fact]
    public void Put_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Put_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId,
            transactionId: 42);

        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Put_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Put_without_callback_emits_7_parts()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        Assert.Equal(7, msg.Parts.Count);
    }

    [Fact]
    public void Put_with_callback_emits_8_parts()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, callbackArgument: 7,
            eventThreadId: ThreadId, eventSequenceId: SeqId);

        Assert.Equal(8, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ASCII_bytes_isObject_zero()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_operation_is_NullObj_DSCode()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        var opPart = msg.Parts[1];
        Assert.Equal((byte)1, opPart.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, opPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_flags_is_i32_zero_isObject_zero()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        var flagsPart = msg.Parts[2];
        Assert.Equal((byte)0, flagsPart.IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, flagsPart.Payload.ToArray());
    }

    [Fact]
    public void Part4_key_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        var keyPart = msg.Parts[3];
        Assert.Equal((byte)1, keyPart.IsObject);
        Assert.Equal(EncodedInt32(Key), keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part5_isDelta_false_is_CacheableBoolean_zero()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

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
            "/test", Key, Value, null, ThreadId, SeqId,
            isDelta: true);

        var isDeltaPart = msg.Parts[4];
        Assert.Equal(
            new byte[] { DSCode.CacheableBoolean, 0x01 },
            isDeltaPart.Payload.ToArray());
    }

    [Fact]
    public void Part6_value_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        // Phase 1.2: values flow through SerializationRegistry, so the
        // value part is DSCode-tagged (IsObject=1), not the cppcache
        // CacheableBytes raw-bytes shortcut (IsObject=0). The shortcut
        // returns once BytesDataConverter lands.
        var valuePart = msg.Parts[5];
        Assert.Equal((byte)1, valuePart.IsObject);
        Assert.Equal(EncodedInt32(Value), valuePart.Payload.ToArray());
    }

    [Fact]
    public void Part7_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value, null,
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
    public void Part8_callback_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Put(
            "/test", Key, Value,
            callbackArgument: 7,
            eventThreadId: ThreadId,
            eventSequenceId: SeqId);

        var cbPart = msg.Parts[7];
        Assert.Equal((byte)1, cbPart.IsObject);
        Assert.Equal(EncodedInt32(7), cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void Put_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Put(null!, Key, Value, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Put("", Key, Value, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Put("/r", null!, Value, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_null_value()
    {
        // cppcache treats null value as Invalidate, not Put — the
        // Invalidate op surfaces later as a dedicated public API.
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Put("/r", Key, null!, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_unregistered_key_type()
    {
        // decimal has no built-in converter (Phase 2 PDX territory),
        // so it's a stable unregistered-type sentinel.
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", 3.14m, Value, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_unregistered_value_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", Key, 3.14m, null, ThreadId, SeqId));
    }

    [Fact]
    public void Put_throws_for_unregistered_callback_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Put("/r", Key, Value,
                callbackArgument: 3.14m,
                eventThreadId: ThreadId, eventSequenceId: SeqId));
    }

    // ====================================================================
    //  Encode round-trip — leverages TcrMessage.Decode + record equality
    // ====================================================================

    [Fact]
    public void Put_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Put(
            "/test", Key, Value, null, ThreadId, SeqId);

        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Put_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Put(
            "/test", Key, Value,
            callbackArgument: 7,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10,
            transactionId: 42,
            isDelta: true);

        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }
}
