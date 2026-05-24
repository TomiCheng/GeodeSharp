using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Verifies the background ping loop fires against a real Apache Geode
/// server. cppcache <c>ThinClientPoolDM::m_pingTask</c> equivalent:
/// once the pool has at least one connected endpoint, the loop sweeps
/// it on each <c>PingInterval</c> tick and records elapsed time in the
/// <c>PingSweepTime</c> Histogram.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class PingIntegrationTests(GeodeFixture fx)
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PingLoop_FiresPeriodically()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        using var capture = new MeterCapture("Geode.Client.Pool", "PingSweepTime");

        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .SetPingInterval(TimeSpan.FromMilliseconds(500))
            .BuildAsync("p", ct);

        // PingSweepTime is a Histogram; Count records each sweep. Poll up
        // to 10s for at least one tick to land.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && capture.Count == 0)
        {
            await Task.Delay(100, ct);
        }

        Assert.True(capture.Count > 0,
            $"Expected PingSweepTime to record ≥1 sweep within 10s, got Count={capture.Count}.");
    }
}
