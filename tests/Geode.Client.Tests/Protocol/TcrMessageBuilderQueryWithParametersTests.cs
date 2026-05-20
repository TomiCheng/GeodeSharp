using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Tests.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.Tests.Protocol;

/// <summary>
/// Wire-shape unit tests for the <c>QueryWithParameters(80)</c>
/// request frame. Mirrors cppcache
/// <c>TcrMessageQueryWithParameters</c>
/// (<c>cppcache/src/TcrMessage.cpp:1769-1806</c>) used by
/// <c>RemoteQuery::execute(paramList)</c>.
/// </summary>
public class TcrMessageBuilderQueryWithParametersTests
{
    private const string Oql = "SELECT * FROM /orders WHERE total > $1";
    private const int CompileTimeout = 15;

    private static TcrMessageBuilder NewBuilder()
    {
        var sp = SerializationTestHelpers.BuildSp();
        return new(new TcrPartBuilder(sp), sp.GetRequiredService<SerializationRegistry>(), sp);
    }

    private static byte[] Int32Be(int v) =>
    [
        (byte)((v >> 24) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)(v & 0xFF),
    ];

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
    public void QueryWithParameters_uses_MessageType_QueryWithParameters()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);
        Assert.Equal(MessageType.QueryWithParameters, msg.MessageType);
    }

    [Fact]
    public void QueryWithParameters_defaults_to_meta_transaction_id()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);
        Assert.Equal(TcrMessageBuilder.MetaTransactionId, msg.TransactionId);
    }

    [Fact]
    public void QueryWithParameters_uses_supplied_transaction_id()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100], transactionId: 42);
        Assert.Equal(42, msg.TransactionId);
    }

    [Fact]
    public void QueryWithParameters_uses_zero_EarlyAck()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);
        Assert.Equal(0, msg.EarlyAck);
    }

    // ====================================================================
    //  Part count
    // ====================================================================

    [Fact]
    public void Default_one_param_emits_5_parts()
    {
        // 3 fixed (querystring / paramCount / compileTimeout) + 1
        // optional timeout + 1 param = 5.
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);
        Assert.Equal(5, msg.Parts.Count);
    }

    [Fact]
    public void Default_two_params_emits_6_parts()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100, "PAID"]);
        Assert.Equal(6, msg.Parts.Count);
    }

    [Fact]
    public void Default_zero_params_emits_4_parts()
    {
        // 3 fixed + 1 optional timeout = 4. Wire still valid;
        // server-side OQL parser decides whether 0-param query is OK.
        var msg = NewBuilder().QueryWithParameters(Oql, []);
        Assert.Equal(4, msg.Parts.Count);
    }

    [Fact]
    public void Null_timeout_omits_timeout_part()
    {
        // 3 fixed + 0 timeout + 1 param = 4. Latent cppcache bug
        // (header always says 4+N) fixed in our impl by computing
        // numOfParts conditionally.
        var msg = NewBuilder().QueryWithParameters(
            Oql, [100], messageResponseTimeoutMillis: null);
        Assert.Equal(4, msg.Parts.Count);
    }

    // ====================================================================
    //  Per-part shape
    // ====================================================================

    [Fact]
    public void Part1_querystring_is_modified_utf8_isObject_zero()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);

        var part = msg.Parts[0];
        Assert.Equal((byte)0, part.IsObject);
        Assert.Equal(
            "SELECT * FROM /orders WHERE total > $1"u8.ToArray(),
            part.Payload.ToArray());
    }

    [Fact]
    public void Part2_paramCount_is_i32_BE_isObject_zero()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100, "PAID", null]);

        var part = msg.Parts[1];
        Assert.Equal((byte)0, part.IsObject);
        Assert.Equal(Int32Be(3), part.Payload.ToArray());
    }

    [Fact]
    public void Part3_compileTimeout_is_constant_15()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);

        var part = msg.Parts[2];
        Assert.Equal((byte)0, part.IsObject);
        // cppcache hard-codes writeIntPart(15) — "COMPILE_QUERY_CLEAR_TIMEOUT".
        Assert.Equal(Int32Be(CompileTimeout), part.Payload.ToArray());
    }

    [Fact]
    public void Part4_responseTimeout_carries_default_15000_ms()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);

        var part = msg.Parts[3];
        Assert.Equal((byte)0, part.IsObject);
        Assert.Equal(Int32Be(15_000), part.Payload.ToArray());
    }

    [Fact]
    public void Part4_responseTimeout_uses_supplied_value()
    {
        var msg = NewBuilder().QueryWithParameters(
            Oql, [100], messageResponseTimeoutMillis: 30_000);

        Assert.Equal(Int32Be(30_000), msg.Parts[3].Payload.ToArray());
    }

    [Fact]
    public void Param_parts_are_DSCode_tagged_isObject_one()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [100]);

        // Part 5 = first param after 3 fixed + 1 timeout.
        var paramPart = msg.Parts[4];
        Assert.Equal((byte)1, paramPart.IsObject);
        Assert.Equal(EncodedInt32(100), paramPart.Payload.ToArray());
    }

    [Fact]
    public void Null_param_serialises_as_DSCode_NullObj()
    {
        var msg = NewBuilder().QueryWithParameters(Oql, [null]);

        var paramPart = msg.Parts[4];
        Assert.Equal((byte)1, paramPart.IsObject);
        // null → single DSCode.NullObj byte, no payload.
        Assert.Equal(new byte[] { DSCode.NullObj }, paramPart.Payload.ToArray());
    }

    // ====================================================================
    //  Arg validation
    // ====================================================================

    [Fact]
    public void QueryWithParameters_throws_for_null_querystring()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().QueryWithParameters(null!, [100]));
    }

    [Fact]
    public void QueryWithParameters_throws_for_empty_querystring()
    {
        Assert.Throws<ArgumentException>(() =>
            NewBuilder().QueryWithParameters("", [100]));
    }

    [Fact]
    public void QueryWithParameters_throws_for_null_parameters()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NewBuilder().QueryWithParameters(Oql, null!));
    }

    [Fact]
    public void QueryWithParameters_throws_for_unregistered_param_type()
    {
        // decimal has no built-in converter — stable unregistered sentinel.
        Assert.Throws<NotSupportedException>(() =>
            NewBuilder().QueryWithParameters(Oql, [3.14m]));
    }

    // ====================================================================
    //  Encode round-trip
    // ====================================================================

    [Fact]
    public void QueryWithParameters_roundtrips_through_encode_decode()
    {
        var original = NewBuilder().QueryWithParameters(Oql, [100, "PAID"]);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void QueryWithParameters_zero_params_roundtrips()
    {
        var original = NewBuilder().QueryWithParameters(Oql, []);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void QueryWithParameters_null_timeout_roundtrips()
    {
        var original = NewBuilder().QueryWithParameters(
            Oql, [100], messageResponseTimeoutMillis: null);
        var decoded = TcrMessage.Decode(original.Encode(), SerializationTestHelpers.BuildSp());
        Assert.Equal(original, decoded);
    }
}
