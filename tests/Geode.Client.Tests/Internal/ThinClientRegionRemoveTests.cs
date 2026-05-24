using System.Buffers.Binary;
using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.RemoveAsync —
/// locks the cppcache <c>TcrMessageDestroy</c> null-value-branch wire
/// layout (<c>TcrMessage.cpp:1974-1985</c>): 5 parts (Region + Key +
/// NullObj(expectedOldValue) + NullObj(operation) + EventId) and the
/// reply decode (entryNotFound i32 in the last reply part: 0 = removed,
/// 1 = absent).
/// </summary>
public class ThinClientRegionRemoveTests
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

    /// <summary>Build a Reply carrying an entryNotFound i32 in its last part (cppcache TcrMessage.cpp:1317-1330).</summary>
    private static TcrMessage MakeDestroyReply(IServiceProvider sp, int entryNotFound)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, entryNotFound);
        return new TcrMessage(sp, MessageType.Reply, TransactionId: -1, EarlyAck: 0,
            [new TcrPart(IsObject: 0, bytes)]);
    }

    private static TcrMessage MakeEmptyReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasFivePartsWithMessageTypeDestroy()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.Destroy, dm.LastRequest.MessageType);
        Assert.Equal(5, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_Part0IsRegionName()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Parts2And3AreNullObj()
    {
        // cppcache TcrMessage.cpp:1979-1980 — expectedOldValue + operation
        // both written as writeObjectPart(nullptr) when caller hands null
        // value/op (which our public Remove API always does).
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        var expectedOldValue = dm.LastRequest!.Parts[2];
        var operation = dm.LastRequest!.Parts[3];
        Assert.Equal(1, expectedOldValue.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, expectedOldValue.Payload.ToArray());
        Assert.Equal(1, operation.IsObject);
        Assert.Equal(new byte[] { DSCode.NullObj }, operation.Payload.ToArray());
    }

    [Fact]
    public async Task WireLayout_Part4IsEventId()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm);

        await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        var eventIdPart = dm.LastRequest!.Parts[4];
        Assert.Equal(0, eventIdPart.IsObject);
        Assert.Equal(18, eventIdPart.Payload.Length);
    }

    // ── Input validation ───────────────────────────────────────────

    [Fact]
    public async Task NullKey_ThrowsArgumentNullException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => region.RemoveAsync(null!, TestContext.Current.CancellationToken));
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Reply_EntryNotFoundZero_ReturnsTrue()
    {
        // entryNotFound=0 → server destroyed the entry → method returns true.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 0) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task Reply_EntryNotFoundOne_ReturnsFalse()
    {
        // entryNotFound=1 → key didn't exist → method returns false.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeDestroyReply(sp, 1) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.RemoveAsync("k1", TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task Exception_MessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeEmptyReply(sp, MessageType.Exception) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.RemoveAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on Remove", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeEmptyReply(sp, MessageType.Ping) };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.RemoveAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
