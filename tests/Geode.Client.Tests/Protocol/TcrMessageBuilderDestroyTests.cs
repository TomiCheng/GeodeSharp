using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>Destroy(9)</c> request frame.
/// Mirrors cppcache <c>TcrMessageDestroy</c>'s <c>value=null,
/// isUserNullValue=false</c> branch (the unconditional destroy path
/// used by <c>destroyNoThrow_remote</c>). Conditional remove
/// (<c>Region::remove(key, value)</c>) lands later and reuses the
/// other branch.
/// </summary>
public class TcrMessageBuilderDestroyTests
{
    private const int Key = 123;
    private const long ThreadId = 1L;
    private const long SeqId = 1L;

    private static TcrMessageBuilder NewBuilder() =>
        new(new TcrPartBuilder(), new SerializationRegistry());

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
    public void Destroy_uses_MessageType_Destroy()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);
        Assert.Equal(MessageType.Destroy, msg.MessageType);
    }

    [Fact]
    public void Destroy_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Destroy_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Destroy(
            "/test", Key, ThreadId, SeqId, transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Destroy_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Destroy_without_callback_emits_5_parts()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);
        Assert.Equal(5, msg.Parts.Count);
    }

    [Fact]
    public void Destroy_with_callback_emits_6_parts()
    {
        var msg = NewBuilder().Destroy(
            "/test", Key, ThreadId, SeqId, callbackArgument: 7);
        Assert.Equal(6, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ASCII_bytes_isObject_zero()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_key_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);

        var keyPart = msg.Parts[1];
        Assert.Equal((byte)1, keyPart.IsObject);
        Assert.Equal(EncodedInt32(Key), keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_expectedOldValue_is_NullObj_DSCode()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);

        var oldValuePart = msg.Parts[2];
        Assert.Equal((byte)1, oldValuePart.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, oldValuePart.Payload.ToArray());
    }

    [Fact]
    public void Part4_operation_is_NullObj_DSCode()
    {
        var msg = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);

        var opPart = msg.Parts[3];
        Assert.Equal((byte)1, opPart.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, opPart.Payload.ToArray());
    }

    [Fact]
    public void Part5_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().Destroy(
            "/test", Key,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10);

        var eventIdPart = msg.Parts[4];
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
    public void Part6_callback_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Destroy(
            "/test", Key, ThreadId, SeqId, callbackArgument: 7);

        var cbPart = msg.Parts[5];
        Assert.Equal((byte)1, cbPart.IsObject);
        Assert.Equal(EncodedInt32(7), cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void Destroy_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Destroy(null!, Key, ThreadId, SeqId));
    }

    [Fact]
    public void Destroy_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Destroy("", Key, ThreadId, SeqId));
    }

    [Fact]
    public void Destroy_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Destroy("/r", null!, ThreadId, SeqId));
    }

    [Fact]
    public void Destroy_throws_for_unregistered_key_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Destroy("/r", 3.14, ThreadId, SeqId));
    }

    [Fact]
    public void Destroy_throws_for_unregistered_callback_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Destroy("/r", Key, ThreadId, SeqId, callbackArgument: 3.14));
    }

    // ====================================================================
    //  Encode round-trip
    // ====================================================================

    [Fact]
    public void Destroy_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Destroy("/test", Key, ThreadId, SeqId);
        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Destroy_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Destroy(
            "/test", Key,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10,
            callbackArgument: 7,
            transactionId: 42);

        var decoded = TcrMessage.Decode(original.Encode());
        Assert.Equal(original, decoded);
    }
}
