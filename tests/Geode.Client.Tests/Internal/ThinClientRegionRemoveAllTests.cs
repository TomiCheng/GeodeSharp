using Geode.Client.Internal;
using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.RemoveAllAsync —
/// locks the cppcache <c>TcrMessageRemoveAll</c> wire layout
/// (<c>TcrMessage.cpp:2424-2468</c>): <c>5 + N</c> parts in the order
/// Region + EventId + flags + NullObj(callback) + count + N×Key, plus
/// reply dispatch matrix.
/// </summary>
public class ThinClientRegionRemoveAllTests
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
        bool cachingEnabled = false,
        bool concurrencyChecksEnabled = false) =>
        new(sp, NullLogger<ThinClientRegion>.Instance, "orders",
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
    public async Task NullKeys_ThrowsArgumentNullException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => region.RemoveAllAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyKeys_ThrowsArgumentException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentException>(
            () => region.RemoveAllAsync(Array.Empty<object>(), TestContext.Current.CancellationToken));
    }

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasFivePlusNPartsWithMessageTypeRemoveAll()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);
        var keys = new object[] { "k1", "k2", "k3" };

        await region.RemoveAllAsync(keys, TestContext.Current.CancellationToken);

        Assert.Equal(MessageType.RemoveAll, dm.LastRequest!.MessageType);
        Assert.Equal(5 + keys.Length, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part2IsFlags_OneForProxyRegion()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm, cachingEnabled: false);

        await region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);

        // cppcache TcrMessage.cpp:2450-2459 — flags is part 2 for RemoveAll
        // (Region=0, EventId=1, flags=2, NullObj(callback)=3, count=4, keys...).
        Assert.Equal(new byte[] { 0, 0, 0, 1 }, dm.LastRequest!.Parts[2].Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part3IsNullObjCallback()
    {
        // cppcache writeObjectPart(aCallbackArgument) — null arg →
        // DSCode.NullObj, IsObject=1.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);

        var cb = dm.LastRequest!.Parts[3];
        Assert.Equal(1, cb.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, cb.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part4IsKeyCount()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Reply) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAllAsync(new object[] { "k1", "k2" }, TestContext.Current.CancellationToken);

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

        await region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Response_MessageType_CompletesSuccessfully()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Response) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on RemoveAll", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.RemoveAllAsync(new object[] { "k1" }, TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
