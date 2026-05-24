using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.ClearAsync —
/// locks the cppcache <c>TcrMessageClearRegion</c> wire layout
/// (<c>TcrMessage.cpp:1644-1682</c>): 2 parts (Region + EventId) and
/// the reply dispatch matrix.
/// </summary>
public class ThinClientRegionClearTests
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
    public async Task WireLayout_HasTwoPartsWithMessageTypeClearRegion()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.ClearAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.ClearRegion, dm.LastRequest.MessageType);
        Assert.Equal(2, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part0IsRegionName()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.ClearAsync(TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part1IsEventId_LongCodeFramedI64Pair()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.ClearAsync(TestContext.Current.CancellationToken);

        var eventIdPart = dm.LastRequest!.Parts[1];
        Assert.Equal(0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
        Assert.Equal(0x03, eventIdPart.Payload.Span[0]);
        Assert.Equal(0x03, eventIdPart.Payload.Span[9]);
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Reply_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.ClearAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ClearAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on Clear", ex.Message);
    }

    [Fact]
    public async Task ClearRegionDataError_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.ClearRegionDataError) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ClearAsync(TestContext.Current.CancellationToken));
        Assert.Contains("ClearRegionDataError", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.ClearAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
