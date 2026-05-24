using Geode.Client.Internal;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Unit-level tests for <see cref="ThinClientRegion"/>.GetAsync — locks
/// the wire layout (cppcache <c>TcrMessageRequest</c>) and the reply
/// dispatch matrix (Response → decode, Exception → throw, missing key
/// → null via empty IsObject=0 part).
/// </summary>
public class ThinClientRegionGetTests
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

    private static TcrMessage MakeValueReply(IServiceProvider sp, GeodeCache cache, object value)
    {
        using var output = ActivatorUtilities.CreateInstance<DataOutput>(sp);
        cache.SerializationRegistry.WriteObjectAsync(output, value).AsTask().GetAwaiter().GetResult();
        var part = new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        return new TcrMessage(sp, MessageType.Response, TransactionId: -1, EarlyAck: 0, [part]);
    }

    /// <summary>
    /// Cache-miss reply: cppcache <c>readObjectPart</c> empty branch —
    /// <c>IsObject=0</c>, zero-length payload → key absent.
    /// </summary>
    private static TcrMessage MakeMissReply(IServiceProvider sp) =>
        new(sp, MessageType.Response, TransactionId: -1, EarlyAck: 0,
            [new TcrPart(IsObject: 0, ReadOnlyMemory<byte>.Empty)]);

    private static TcrMessage MakeEmptyReply(IServiceProvider sp, MessageType messageType) =>
        new(sp, messageType, TransactionId: -1, EarlyAck: 0, []);

    // ── Wire layout ────────────────────────────────────────────────

    [Fact]
    public async Task WireLayout_HasTwoPartsWithMessageTypeRequest()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeValueReply(sp, cache, "v") };
        var region = MakeRegion(sp, cache, dm);

        await region.GetAsync("k1", TestContext.Current.CancellationToken);

        Assert.NotNull(dm.LastRequest);
        Assert.Equal(MessageType.Request, dm.LastRequest.MessageType);
        Assert.Equal(2, dm.LastRequest.Parts.Count);
    }

    [Fact]
    public async Task WireLayout_RegionNamePartCarriesFullPath()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeValueReply(sp, cache, "v") };
        var region = MakeRegion(sp, cache, dm, name: "orders");

        await region.GetAsync("k1", TestContext.Current.CancellationToken);

        var regionPart = dm.LastRequest!.Parts[0];
        Assert.Equal(0, regionPart.IsObject);
        Assert.Equal("/orders"u8.ToArray(), regionPart.Payload.ToArray());
    }

    // ── Reply dispatch ─────────────────────────────────────────────

    [Fact]
    public async Task Response_DSCodeTaggedValue_DecodesRoundTrip()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeValueReply(sp, cache, "hello") };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.GetAsync("k1", TestContext.Current.CancellationToken);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task Response_DSCodeTaggedInt32_DecodesAsInt()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeValueReply(sp, cache, 42) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.GetAsync("k1", TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Response_EmptyIsObjectZero_ReturnsNull()
    {
        // Cache miss: cppcache readObjectPart payloadEmpty + IsObject=0 → null.
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache) { CannedReply = MakeMissReply(sp) };
        var region = MakeRegion(sp, cache, dm);

        var result = await region.GetAsync("k1", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Response_ZeroParts_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache)
        {
            CannedReply = MakeEmptyReply(sp, MessageType.Response),
        };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.GetAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Response with zero parts", ex.Message);
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
            () => region.GetAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Server exception on Get", ex.Message);
    }

    [Fact]
    public async Task UnknownMessageType_ThrowsGeodeException()
    {
        await using var sp = BuildSp();
        var cache = MakeCache(sp);
        var dm = new FakeThinClientBaseDM(sp, cache)
        {
            CannedReply = MakeEmptyReply(sp, MessageType.Ping),
        };
        var region = MakeRegion(sp, cache, dm);

        var ex = await Assert.ThrowsAsync<GeodeException>(
            () => region.GetAsync("k1", TestContext.Current.CancellationToken));
        Assert.Contains("Unexpected reply type", ex.Message);
    }
}
