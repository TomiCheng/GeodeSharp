/*
using Geode.Client.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end smoke test: TcrConnection.ConnectAsync (TCP + handshake)
/// followed by PingAsync against a real Apache Geode server running in
/// the shared <see cref="GeodeFixture"/> container. This is the moment
/// of truth for Phase 2 — if any byte in the handshake is wrong, the
/// server will refuse the connection here and the assertion / exception
/// tells us what to fix.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class PingIntegrationTests(GeodeFixture fx)
{
    /// <summary>
    /// Generous enough to absorb cold-start IO and image-pull effects on a
    /// CI runner; tight enough that a genuine deadlock surfaces in seconds
    /// rather than the xUnit-default 10-minute timeout.
    /// </summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task PingAsync_succeeds_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Empty config — every GeodeClientOptions field falls back to its
        // declared default, which is what the MVP path should require to
        // work against a stock Geode server.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config)
            .BuildServiceProvider();

        // TcrConnection isn't a DI service (stateful resource — owns
        // socket / stream / handshake state). Build it through
        // ActivatorUtilities so its 4 ctor deps resolve from sp, same
        // pattern as the production path in
        // TcrEndpoint.CreateNewConnectionAsync.
        var connection = ActivatorUtilities.CreateInstance<TcrConnection>(services);

        // ConnectAsync bundles TCP connect + Geode handshake. Failure
        // here surfaces as GeodeException (server refused) or IOException
        // (transport / framing bug).
        await connection.ConnectAsync(fx.LocatorHost, fx.ServerPort, cancellationToken: cts.Token);

        // Ping a real server-cache; successful return = the server
        // accepted the handshake AND replied with MessageType.Reply (6).
        await connection.PingAsync(cts.Token);
    }
}

*/