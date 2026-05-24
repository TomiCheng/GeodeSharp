using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.GetAllAsync —
/// locks the cppcache <c>TcrMessageGetAll</c> wire layout
/// (<c>TcrMessage.cpp:2470-2502</c>): 3 parts (Region +
/// keys-as-CacheableObjectArray + int(0) callback placeholder) and the
/// reply dispatch matrix.
/// Success-path value decoding lives in the integration test (real
/// chunked reply); here we lock layout + error paths.
/// </summary>
public class ThinClientRegionGetAllTests
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
        IServiceProvider sp, GeodeCache cache, FakeThinClientBaseDM dm) =>
        new(sp, NullLogger<ThinClientRegion>.Instance, "orders", new RegionAttributes(), dm);

    private static TcrMessage MakeReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Input validation ───────────────────────────────────────────

    [Fact]
    public async Task NullKeys_ThrowsArgumentNullException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => region.GetAllAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyKeys_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentException>(
            () => region.GetAllAsync(Array.Empty<object>(), TestContext.Current.CancellationToken));
    }

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasThreePartsWithMessageTypeGetAll70()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await region.GetAllAsync(new object[] { "k1", "k2" }, TestContext.Current.CancellationToken);

        Assert.Equal(MessageType.GetAll70, dm.LastRequest!.MessageType);
        Assert.Equal(3, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part1IsCacheableObjectArrayDSCode()
    {
        // cppcache writeObjectPart(nullptr, false, false, m_keyList) —
        // serializes keys as CacheableObjectArray (DSCode 52).
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await region.GetAllAsync(new object[] { "k1", "k2" }, TestContext.Current.CancellationToken);

        var keysPart = dm.LastRequest!.Parts[1];
        Assert.Equal(1, keysPart.IsObject);
        Assert.Equal(DSCode.CacheableObjectArray, keysPart.Payload.Span[0]);
    }

    [Fact]
    public async Task WireLayout_Part2IsCallbackPlaceholderIntZero()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await region.GetAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);

        Assert.Equal(0, dm.LastRequest!.Parts[2].IsObject);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, dm.LastRequest.Parts[2].Payload.ToArray());
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Response_NoChunks_ReturnsEmptyValuesDict()
    {
        // No chunks staged → chunkedResult.Values stays empty.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.GetAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.GetAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on GetAll", ex.Message);
    }

    [Fact]
    public async Task GetAllDataError_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.GetAllDataError) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.GetAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken));
        Assert.Contains("GetAllDataError", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.GetAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
