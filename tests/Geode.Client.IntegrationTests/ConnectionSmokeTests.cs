using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Walking-skeleton end-to-end: build a cache + pool against a real Geode
/// server and verify the client at least reaches a "connected" state via
/// the background ConnManageLoop.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class ConnectionSmokeTests(GeodeFixture fx, IGeodeCacheFactory factory)
{
    [Fact]
    public async Task BuildAsync_AgainstRealServer_OpensConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        //await using var sp = BuildSp();
        using var capture = new MeterCapture("Geode.Client.Pool", "PoolConnections");

        var cache = await factory.CreateAsync("c", ct: ct);
        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .BuildAsync("p", ct);

        // ConnManageLoop opens conns fire-and-forget; poll up to 5s for the
        // background RestoreMinConnections tick to land a connected endpoint.
        // PoolConnections is an ObservableGauge — Observe() pulls the value.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            capture.Observe();
            if (capture.LastValue > 0) break;
            await Task.Delay(100, ct);
        }

        Assert.True(capture.LastValue > 0,
            $"Expected PoolConnections > 0 after 5s, got {capture.LastValue}.");
    }
}
