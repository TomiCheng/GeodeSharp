/*
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>ClearRegion(36)</c> request frame.
/// Mirrors cppcache <c>TcrMessageClearRegion</c>
/// (<c>cppcache/src/TcrMessage.cpp:1644-1682</c>) used by
/// <c>ThinClientRegion::clear</c>. Region-wide op — no key part; the
/// optional <c>millisecondsResponseTimeout</c> part stays unset because
/// cppcache callers hard-code <c>-1</c>.
/// </summary>
public class TcrMessageBuilderClearRegionTests
{
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
    public void ClearRegion_uses_MessageType_ClearRegion()
    {
        var msg = NewBuilder().ClearRegion("/test", ThreadId, SeqId);
        Assert.Equal(MessageType.ClearRegion, msg.MessageType);
    }

    [Fact]
    public void ClearRegion_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().ClearRegion("/test", ThreadId, SeqId);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void ClearRegion_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().ClearRegion(
            "/test", ThreadId, SeqId, transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void ClearRegion_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().ClearRegion("/test", ThreadId, SeqId);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void ClearRegion_without_callback_emits_2_parts()
    {
        var msg = NewBuilder().ClearRegion("/test", ThreadId, SeqId);
        Assert.Equal(2, msg.Parts.Count);
    }

    [Fact]
    public void ClearRegion_with_callback_emits_3_parts()
    {
        var msg = NewBuilder().ClearRegion(
            "/test", ThreadId, SeqId, callbackArgument: 7);
        Assert.Equal(3, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ASCII_bytes_isObject_zero()
    {
        var msg = NewBuilder().ClearRegion("/test", ThreadId, SeqId);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_eventId_is_18_bytes_with_threadId_and_seqId()
    {
        var msg = NewBuilder().ClearRegion(
            "/test",
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10);

        var eventIdPart = msg.Parts[1];
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
    public void Part3_callback_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().ClearRegion(
            "/test", ThreadId, SeqId, callbackArgument: 7);

        var cbPart = msg.Parts[2];
        Assert.Equal((byte)1, cbPart.IsObject);
        Assert.Equal(EncodedInt32(7), cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void ClearRegion_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().ClearRegion(null!, ThreadId, SeqId));
    }

    [Fact]
    public void ClearRegion_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().ClearRegion("", ThreadId, SeqId));
    }

    [Fact]
    public void ClearRegion_throws_for_unregistered_callback_type()
    {
        // decimal has no built-in converter — stable unregistered sentinel.
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().ClearRegion("/r", ThreadId, SeqId, callbackArgument: 3.14m));
    }

    // ====================================================================
    //  Encode round-trip
    // ====================================================================

    [Fact]
    public void ClearRegion_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().ClearRegion("/test", ThreadId, SeqId);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void ClearRegion_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().ClearRegion(
            "/test",
            eventThreadId: 0x0102030405060708,
            eventSequenceId: 0x090A0B0C0D0E0F10,
            callbackArgument: 7,
            transactionId: 42);

        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }
}

*/