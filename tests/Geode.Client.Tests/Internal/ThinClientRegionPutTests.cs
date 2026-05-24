using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.PutAsync — locks
/// the cppcache <c>TcrMessagePut</c> wire layout
/// (<c>cppcache/src/TcrMessage.cpp:1989-2033</c>): 7 parts in the order
/// region + null-op + flags + key + isDelta + value + eventId, and the
/// reply dispatch matrix.
/// </summary>
public class ThinClientRegionPutTests
{
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

    private static TcrMessage MakeReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasSevenPartsWithMessageTypePut()
    {
        // cppcache TcrMessage.cpp:1999, 2021-2032 — m_msgType = PUT;
        // 7 parts in order: Region + NullOp + Flags + Key + IsDelta +
        // Value + EventId (no callback).
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.Put, dm.LastRequest.MessageType);
        Assert.Equal(7, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_RegionNamePartCarriesFullPath()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part1IsNullOp_DSCode41()
    {
        // cppcache writeObjectPart(nullptr) → single-byte DSCode.NullObj,
        // IsObject=1.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        var nullOp = dm.LastRequest!.Parts[1];
        Assert.Equal(1, nullOp.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, nullOp.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part2IsFlagsInt32Zero()
    {
        // cppcache writeIntPart(0) — i32 BE = 0, IsObject=0.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        var flagsPart = dm.LastRequest!.Parts[2];
        Assert.Equal(0, flagsPart.IsObject);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00 }, flagsPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part4IsCacheableBooleanIsDeltaFalse()
    {
        // cppcache writeObjectPart(CacheableBoolean::create(false)) —
        // [DSCode 53, 0x00], IsObject=1.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        var isDeltaPart = dm.LastRequest!.Parts[4];
        Assert.Equal(1, isDeltaPart.IsObject);
        Assert.Equal(new byte[] { DSCode.CacheableBoolean, 0x00 }, isDeltaPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part6IsEventId_LongCodeFramedI64Pair()
    {
        // cppcache EventId::writeIdsData (EventId.hpp:95-107):
        // [0x03][threadId i64 BE][0x03][sequenceId i64 BE], IsObject=0.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);

        var eventIdPart = dm.LastRequest!.Parts[6];
        Assert.Equal(0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
        var bytes = eventIdPart.Payload.Span;
        Assert.Equal(0x03, bytes[0]);   // longCode for threadId
        Assert.Equal(0x03, bytes[9]);   // longCode for sequenceId
        // ThreadId is EventIdGenerator.ThreadId const = 1; first Next() bumps SequenceId to 1.
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bytes[1..9].ToArray());
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, bytes[10..18].ToArray());
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Reply_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        // No exception → success.
        await region.PutAsync("k1", "v1", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.PutAsync("k1", "v1", TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on Put", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.PutAsync("k1", "v1", TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
