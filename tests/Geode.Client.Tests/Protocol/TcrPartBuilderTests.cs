using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrPartBuilderTests
{
    [Fact]
    public async Task RawBytes_BuildAsync_ReturnsPartWithIsObjectZero()
    {
        var builder = TcrPartBuilder.RawBytes(new byte[] { 0xAB, 0xCD });

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, part.IsObject);
    }

    [Fact]
    public async Task RawBytes_BuildAsync_PreservesPayload()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var builder = TcrPartBuilder.RawBytes(payload);

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(payload, part.Payload.ToArray());
    }

    [Fact]
    public async Task RawBytes_EmptyPayload_IsAllowed()
    {
        var builder = TcrPartBuilder.RawBytes(ReadOnlyMemory<byte>.Empty);

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, part.IsObject);
        Assert.True(part.Payload.IsEmpty);
    }

    [Fact]
    public async Task LambdaCtor_BuildAsync_InvokesFunc()
    {
        var invoked = 0;
        var builder = new TcrPartBuilder(_ =>
        {
            invoked++;
            return ValueTask.FromResult(new TcrPart(1, new byte[] { 0x42 }));
        });

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, invoked);
        Assert.Equal(1, part.IsObject);
        Assert.Equal(new byte[] { 0x42 }, part.Payload.ToArray());
    }

    [Fact]
    public async Task BuildAsync_PassesCancellationTokenThrough()
    {
        using var cts = new CancellationTokenSource();
        var observed = CancellationToken.None;
        var builder = new TcrPartBuilder(ct =>
        {
            observed = ct;
            return ValueTask.FromResult(new TcrPart(0, ReadOnlyMemory<byte>.Empty));
        });

        await builder.BuildAsync(cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task KeepAlive_True_EmitsSingleOneByte()
    {
        var builder = TcrPartBuilder.KeepAlive(true);

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0x01 }, part.Payload.ToArray());
    }

    [Fact]
    public async Task KeepAlive_False_EmitsSingleZeroByte()
    {
        var builder = TcrPartBuilder.KeepAlive(false);

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 0x00 }, part.Payload.ToArray());
    }

    [Fact]
    public async Task KeepAlive_IsObjectZero()
    {
        var builder = TcrPartBuilder.KeepAlive(true);

        var part = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, part.IsObject);
    }
}
