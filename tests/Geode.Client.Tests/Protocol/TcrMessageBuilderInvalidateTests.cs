using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>Invalidate(83)</c> request frame.
/// Mirrors cppcache <c>TcrMessageInvalidate</c>
/// (<c>cppcache/src/TcrMessage.cpp:1896-1932</c>) used by
/// <c>ThinClientRegion::invalidateNoThrow_remote</c>.
/// </summary>
public class TcrMessageBuilderInvalidateTests
{
    private const int Key = 123;
    private const long ThreadId = 1L;
    private const long SeqId = 1L;

    private static TcrMessageBuilder NewBuilder()
    {
        var sp = SerializationTestHelpers.BuildSp();
        return new(new TcrPartBuilder(sp), sp.GetRequiredService<SerializationRegistry>(), sp);
    }

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
    public void Invalidate_uses_MessageType_Invalidate()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);
        Assert.Equal(MessageType.Invalidate, msg.MessageType);
    }

    [Fact]
    public void Invalidate_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Invalidate_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Invalidate(
            "/test", Key, ThreadId, SeqId, transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Invalidate_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Invalidate_without_callback_emits_3_parts()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);
        Assert.Equal(3, msg.Parts.Count);
    }

    [Fact]
    public void Invalidate_with_callback_emits_4_parts()
    {
        var msg = NewBuilder().Invalidate(
            "/test", Key, ThreadId, SeqId, callbackArgument: 7);
        Assert.Equal(4, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ASCII_bytes_isObject_zero()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_key_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);

        var keyPart = msg.Parts[1];
        Assert.Equal((byte)1, keyPart.IsObject);
        Assert.Equal(EncodedInt32(Key), keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().Invalidate(
            "/test", Key,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10);

        var eventIdPart = msg.Parts[2];
        Assert.Equal((byte)0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
        Assert.Equal(
            new byte[] {
                0x03, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x03, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            },
            eventIdPart.Payload.ToArray());
    }

    [Fact]
    public void Part4_callback_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Invalidate(
            "/test", Key, ThreadId, SeqId, callbackArgument: 7);

        var cbPart = msg.Parts[3];
        Assert.Equal((byte)1, cbPart.IsObject);
        Assert.Equal(EncodedInt32(7), cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void Invalidate_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Invalidate(null!, Key, ThreadId, SeqId));
    }

    [Fact]
    public void Invalidate_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Invalidate("", Key, ThreadId, SeqId));
    }

    [Fact]
    public void Invalidate_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Invalidate("/r", null!, ThreadId, SeqId));
    }

    [Fact]
    public void Invalidate_throws_for_unregistered_key_type()
    {
        // decimal has no built-in converter (Phase 2 PDX territory),
        // stable unregistered-type sentinel.
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Invalidate("/r", 3.14m, ThreadId, SeqId));
    }

    [Fact]
    public void Invalidate_throws_for_unregistered_callback_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Invalidate("/r", Key, ThreadId, SeqId, callbackArgument: 3.14m));
    }

    // ====================================================================
    //  Encode round-trip
    // ====================================================================

    [Fact]
    public void Invalidate_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Invalidate("/test", Key, ThreadId, SeqId);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Invalidate_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Invalidate(
            "/test", Key,
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10,
            callbackArgument: 7,
            transactionId: 42);

        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }
}
