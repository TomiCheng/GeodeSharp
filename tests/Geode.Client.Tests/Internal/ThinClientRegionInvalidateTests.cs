using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.InvalidateAsync —
/// locks the cppcache <c>TcrMessageInvalidate</c> wire layout
/// (<c>TcrMessage.cpp:1896-1932</c>): 3 parts (Region + Key + EventId)
/// and the reply dispatch matrix (Reply / Exception / InvalidateError /
/// unknown).
/// </summary>
public class ThinClientRegionInvalidateTests
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
    public async Task WireLayout_HasThreePartsWithMessageTypeInvalidate()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.InvalidateAsync("k1", TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.Invalidate, dm.LastRequest.MessageType);
        Assert.Equal(3, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part0IsRegionName()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.InvalidateAsync("k1", TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part2IsEventId()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.InvalidateAsync("k1", TestContext.Current.CancellationToken);

        var eventIdPart = dm.LastRequest!.Parts[2];
        Assert.Equal(0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
    }

    // ── Input validation ───────────────────────────────────────────

    [Fact]
    public async Task NullKey_ThrowsArgumentNullException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => region.InvalidateAsync(null!, TestContext.Current.CancellationToken));
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Reply_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.InvalidateAsync("k1", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.InvalidateAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on Invalidate", ex.Message);
    }

    [Fact]
    public async Task InvalidateError_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.InvalidateError) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.InvalidateAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("InvalidateError", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.InvalidateAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
