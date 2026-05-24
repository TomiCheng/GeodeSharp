using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Verifies the locator-mode background refresh loop fires against a
/// real locator. cppcache <c>ThinClientPoolDM::m_updateLocatorListTask</c>
/// equivalent: every <c>UpdateLocatorListInterval</c> tick the pool
/// sends a <c>LocatorListRequest</c> RPC to learn newly-added locators
/// and drop dead ones; elapsed time is recorded in the
/// <c>LocatorListRequestTime</c> Histogram.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class LocatorModeIntegrationTests(GeodeFixture fx)
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
    public async Task LocatorUpdateLoop_FiresPeriodically()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var sp = BuildSp();
        using var capture = new MeterCapture("Geode.Client.Pool", "LocatorListRequestTime");

        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        await cache.PoolManager.CreateFactory()
            .AddLocator(fx.LocatorHost, fx.LocatorPort)
            .SetUpdateLocatorListInterval(TimeSpan.FromMilliseconds(500))
            .BuildAsync("p", ct);

        // LocatorListRequestTime is a Histogram; Count records each RPC.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && capture.Count == 0)
        {
            await Task.Delay(100, ct);
        }

        Assert.True(capture.Count > 0,
            $"Expected LocatorListRequestTime to record ≥1 RPC within 10s, got Count={capture.Count}.");
    }
}
