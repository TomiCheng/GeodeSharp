using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.ContainsKeyAsync —
/// locks the wire layout (cppcache <c>TcrMessage.cpp:1808-1842</c>) and
/// the reply dispatch matrix. A fake <see cref="ThinClientBaseDM"/>
/// captures the request and serves canned replies so we test the region
/// in isolation, no pool / no socket.
/// </summary>
public class ThinClientRegionContainsKeyTests
{
    // ── DI host ────────────────────────────────────────────────────

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static GeodeCache MakeCache(IServiceProvider sp) =>
        ActivatorUtilities.CreateInstance<GeodeCache>(sp, "c");

    private static ThinClientRegion MakeRegion(
        IServiceProvider sp, GeodeCache cache, FakeThinClientBaseDM dm,
        string name = "orders") =>
        new(sp, NullLogger<ThinClientRegion>.Instance, name, new RegionAttributes(), dm);

    /// <summary>Build a <see cref="MessageType.Response"/> message carrying a single CacheableBoolean part.</summary>
    private static TcrMessage MakeBoolReply(IServiceProvider sp, GeodeCache cache, bool value)
    {
        using var output = ActivatorUtilities.CreateInstance<DataOutput>(sp);
        cache.SerializationRegistry.WriteObjectAsync(output, value).AsTask().GetAwaiter().GetResult();
        var part = new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        return new TcrMessage(sp, MessageType.Response, TransactionId: -1, EarlyAck: 0, [part]);
    }

    /// <summary>Build a non-bool Response payload (encoded <see cref="int"/>) for the negative test.</summary>
    private static TcrMessage MakeInt32Reply(IServiceProvider sp, GeodeCache cache, int value)
    {
        using var output = ActivatorUtilities.CreateInstance<DataOutput>(sp);
        cache.SerializationRegistry.WriteObjectAsync(output, value).AsTask().GetAwaiter().GetResult();
        var part = new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        return new TcrMessage(sp, MessageType.Response, TransactionId: -1, EarlyAck: 0, [part]);
    }

    private static TcrMessage MakeEmptyReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Wire layout (regression lock for the op-flag bug) ──────────

    [Fact]
    public async Task WireLayout_HasThreePartsWithMessageTypeContainsKey()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeBoolReply(sp, cache, true) };
        var region = MakeRegion(sp, cache, dm);

        await region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.ContainsKey, dm.LastRequest.MessageType);
        Assert.Equal(3, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_OpFlagPartIsZero_NotOne()
    {
        // Regression lock for the wire bug previously here: cppcache
        // TcrMessage.cpp:1837 — 0 = containsKey, 1 = containsValueForKey.
        // ContainsKeyAsync must send 0; ContainsValueForKeyAsync (when
        // it ships) will send 1.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeBoolReply(sp, cache, true) };
        var region = MakeRegion(sp, cache, dm);

        await region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken);

        // Part 3 (index 2) is a raw int32 BE op-flag, IsObject=0.
        var opFlagPart = dm.LastRequest!.Parts[2];
        Assert.Equal(0, opFlagPart.IsObject);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, opFlagPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_RegionNamePartCarriesFullPath()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeBoolReply(sp, cache, true) };
        var region = MakeRegion(sp, cache, dm, name: "orders");
        // ThinClientRegion is constructed with parent=null → FullPath = "/orders".

        await region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        // Modified UTF-8 for pure ASCII == ASCII bytes verbatim.
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Response_TrueBool_ReturnsTrue()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeBoolReply(sp, cache, true) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task Response_FalseBool_ReturnsFalse()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeBoolReply(sp, cache, false) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task Response_NonBoolPayload_ThrowsGeodeException()
    {
        // Server reply with the wrong CLR type in Parts[0] (here: int).
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeInt32Reply(sp, cache, 42) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("expected bool reply", ex.Message);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache)
        {
            CannedReply = MakeEmptyReply(sp, MessageType.Exception),
        };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on ContainsKey", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        // Anything outside Response / Exception falls into the default
        // arm — defensive check against a broken codec or server bug.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache)
        {
            CannedReply = MakeEmptyReply(sp, MessageType.Ping),
        };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ContainsKeyAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }

}
