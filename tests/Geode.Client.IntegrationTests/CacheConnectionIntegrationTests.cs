using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.1 end-to-end test: open one TCP connection to a real
/// Apache Geode server through the public <see cref="IGeodeCache"/>
/// API, then close it cleanly. No pool, no multi-endpoint, no
/// failover &#x2014; just the consumer-visible
/// <c>EnsureInitializedAsync</c> / <c>CloseAsync</c> contract.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CacheConnectionIntegrationTests(GeodeFixture fx)
{
    private readonly GeodeFixture _fx = fx;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Path-(a) declarative config: one named pool with one server
    /// pointing at the fixture container. Equivalent to a cache.xml
    /// <c>&lt;pool&gt;&lt;server host="..." port="..."/&gt;&lt;/pool&gt;</c>.
    /// </summary>
    private void ConfigureCacheXml(GeodeClientOptions config)
    {
        config.CacheXml = new CacheXmlOptions
        {
            Pools =
            {
                new CacheXmlPoolOptions
                {
                    Name = "testPool",
                    Servers =
                    {
                        new CacheXmlHostPort
                        {
                            Host = _fx.LocatorHost,
                            Port = _fx.ServerPort,
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public async Task EnsureInitializedAsync_opens_connection_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCache>();
        Assert.False(cache.IsClosed);

        // Phase 1.1 goal: open a single TCP connection, run handshake,
        // become reachable for Ping / future ops. Should not throw.
        await cache.EnsureInitializedAsync(cts.Token);

        // Idempotent — second call must not re-handshake or fail.
        await cache.EnsureInitializedAsync(cts.Token);

        // Phase 1.1 goal: send CloseConnection(18), drain in-flight,
        // dispose endpoint cleanly.
        await cache.CloseAsync(cts.Token);

        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task CloseAsync_is_idempotent()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCache>();
        await cache.EnsureInitializedAsync(cts.Token);

        await cache.CloseAsync(cts.Token);
        await cache.CloseAsync(cts.Token); // second call: no-op, must not throw

        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task DisposeAsync_closes_underlying_connection()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        IGeodeCache cache;
        await using (var services = new ServiceCollection()
                         .AddLogging()
                         .AddGeodeClient(ConfigureCacheXml)
                         .BuildServiceProvider())
        {
            cache = services.GetRequiredService<IGeodeCache>();
            await cache.EnsureInitializedAsync(cts.Token);
        }
        // ServiceProvider disposal cascades into the
        // GeodeCacheFactory's per-cache scope, which disposes Cache,
        // which disposes the TcrEndpoint, which sends
        // CloseConnection(18) and closes the socket.

        Assert.True(cache.IsClosed);
    }
}
