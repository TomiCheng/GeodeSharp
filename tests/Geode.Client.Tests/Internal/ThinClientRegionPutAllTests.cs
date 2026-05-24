using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.PutAllAsync —
/// locks the cppcache <c>TcrMessagePutAll</c> wire layout
/// (<c>TcrMessage.cpp:2354-2422</c>): <c>5 + 2N</c> parts in the order
/// Region + EventId + reserved(0) + flags + count + N×(Key, Value),
/// plus reply dispatch matrix.
/// </summary>
public class ThinClientRegionPutAllTests
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
        string name = "orders", bool cachingEnabled = false,
        bool concurrencyChecksEnabled = false) =>
        new(sp, NullLogger<ThinClientRegion>.Instance, name,
            new RegionAttributes
            {
                CachingEnabled = cachingEnabled,
                ConcurrencyChecksEnabled = concurrencyChecksEnabled,
            },
            dm);

    private static TcrMessage MakeReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Input validation ───────────────────────────────────────────

    [Fact]
    public async Task NullMap_ThrowsArgumentNullException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => region.PutAllAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyMap_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentException>(
            () => region.PutAllAsync(
                new Dictionary<object, object>(), TestContext.Current.CancellationToken));
    }

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasFivePlus2NPartsWithMessageTypePutAll()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);
        var map = new Dictionary<object, object> { ["k1"] = "v1", ["k2"] = "v2", ["k3"] = "v3" };

        await region.PutAllAsync(map, TestContext.Current.CancellationToken);

        Assert.Equal(MessageType.PutAll, dm.LastRequest!.MessageType);
        Assert.Equal(5 + 2 * map.Count, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part0IsRegionName()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part2IsReservedIntZero()
    {
        // cppcache TcrMessage.cpp:2390 — writeIntPart(0) reserved slot.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken);

        Assert.Equal(0, dm.LastRequest!.Parts[2].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, dm.LastRequest.Parts[2].Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part3IsFlags_OneForProxyRegion()
    {
        // cppcache flags: 1 = EMPTY (caching disabled) → proxy regions.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, cachingEnabled: false);

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0, 0, 0, 1 }, dm.LastRequest!.Parts[3].Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part4IsEntryCount()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1", ["k2"] = "v2" },
            TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0, 0, 0, 2 }, dm.LastRequest!.Parts[4].Payload.ToArray());
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Reply_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Response_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await region.PutAllAsync(
            new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.PutAllAsync(
                new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on PutAll", ex.Message);
    }

    [Fact]
    public async Task PutDataError_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.PutDataError) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.PutAllAsync(
                new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken));
        Assert.Contains("PutDataError", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.PutAllAsync(
                new Dictionary<object, object> { ["k1"] = "v1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
