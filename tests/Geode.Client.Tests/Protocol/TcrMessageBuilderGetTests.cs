using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Phase 1.2 walking-skeleton scope: int32 keys only (the registry
/// ships <c>Int32DataConverter</c> + <c>BooleanDataConverter</c>).
/// String / byte[] / Date / collection coverage lands as their
/// converters do.
/// </summary>
public class TcrMessageBuilderGetTests
{
    private const int Key = 123;

    private static TcrMessageBuilder NewBuilder()
    {
        var sp = SerializationTestHelpers.BuildSp();
        return new(new TcrPartBuilder(sp), sp.GetRequiredService<SerializationRegistry>(), sp);
    }

    // Helper: the on-wire bytes for an int32 key (CacheableInt32(57) +
    // 4-byte big-endian payload). Mirrors what Int32DataConverter
    // writes via SerializationRegistry.WriteObject.
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
    public void Get_uses_MessageType_Request()
    {
        var msg = NewBuilder().Get("/test", Key);
        Assert.Equal(MessageType.Request, msg.MessageType);
    }

    [Fact]
    public void Get_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().Get("/test", Key);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void Get_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().Get("/test", Key, transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void Get_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().Get("/test", Key);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Get_without_callback_emits_2_parts()
    {
        var msg = NewBuilder().Get("/test", Key);
        Assert.Equal(2, msg.Parts.Count);
    }

    [Fact]
    public void Get_with_callback_emits_3_parts()
    {
        var msg = NewBuilder().Get("/test", Key, callbackArgument: 7);
        Assert.Equal(3, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_region_is_raw_ASCII_bytes_isObject_zero()
    {
        var msg = NewBuilder().Get("/test", Key);

        var regionPart = msg.Parts[0];
        Assert.Equal((byte)0, regionPart.IsObject);
        Assert.Equal("/test"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public void Part2_key_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Get("/test", Key);

        var keyPart = msg.Parts[1];
        Assert.Equal((byte)1, keyPart.IsObject);
        // DSCode.CacheableInt32(57) + i32 BE 123 == 0x00 00 00 7B.
        Assert.Equal(EncodedInt32(Key), keyPart.Payload.ToArray());
    }

    [Fact]
    public void Part3_callback_is_DSCode_tagged_CacheableInt32()
    {
        var msg = NewBuilder().Get("/test", Key, callbackArgument: 7);

        var cbPart = msg.Parts[2];
        Assert.Equal((byte)1, cbPart.IsObject);
        Assert.Equal(EncodedInt32(7), cbPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void Get_throws_for_null_regionName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Get(null!, Key));
    }

    [Fact]
    public void Get_throws_for_empty_regionName()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().Get("", Key));
    }

    [Fact]
    public void Get_throws_for_null_key()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().Get("/r", null!));
    }

    [Fact]
    public void Get_throws_for_unregistered_key_type()
    {
        // SerializationRegistry has no converter for decimal — Java's
        // counterpart BigDecimal is Phase 2 PDX territory, so this
        // sentinel stays stable across the Tier A built-in expansion.
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Get("/r", 3.14m));
    }

    [Fact]
    public void Get_throws_for_unregistered_callback_type()
    {
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().Get("/r", Key, callbackArgument: 3.14m));
    }

    // ====================================================================
    //  Encode round-trip — leverages TcrMessage.Decode + record equality
    // ====================================================================

    [Fact]
    public void Get_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Get("/test", Key);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Get_with_callback_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().Get(
            "/test", Key, callbackArgument: 7, transactionId: 99);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }
}
