using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.Tests.Protocol;

public class TcrMessageBuilderTests
{
    private static ServiceProvider BuildSp() =>
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Create_ReturnsBuilder()
    {
        using var sp = BuildSp();

        var builder = TcrMessageBuilder.Create(sp, MessageType.Ping);

        Assert.NotNull(builder);
    }

    [Fact]
    public async Task BuildAsync_NoParts_ReturnsMessageWithEmptyParts()
    {
        using var sp = BuildSp();
        var builder = TcrMessageBuilder.Create(sp, MessageType.Ping);

        var message = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Empty(message.Parts);
    }

    [Fact]
    public async Task BuildAsync_PreservesMessageType()
    {
        using var sp = BuildSp();
        var builder = TcrMessageBuilder.Create(sp, MessageType.Ping);

        var message = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(MessageType.Ping, message.MessageType);
    }

    [Fact]
    public async Task BuildAsync_DefaultTransactionId_IsMinusOne()
    {
        // -1 sentinel for "no Geode transaction" — mirrors cppcache
        // TcrMessage::writeHeader (m_txId = -1 when no TxState).
        using var sp = BuildSp();
        var builder = TcrMessageBuilder.Create(sp, MessageType.Ping);

        var message = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal(-1, message.TransactionId);
    }

    [Fact]
    public async Task BuildAsync_DefaultEarlyAck_IsZero()
    {
        using var sp = BuildSp();
        var builder = TcrMessageBuilder.Create(sp, MessageType.Ping);

        var message = await builder.BuildAsync(TestContext.Current.CancellationToken);

        Assert.Equal((byte)0, message.EarlyAck);
    }
}
