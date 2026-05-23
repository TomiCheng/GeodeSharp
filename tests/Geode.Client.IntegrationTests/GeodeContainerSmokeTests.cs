/*
using Xunit;

namespace Geode.Client.IntegrationTests;

[Collection(nameof(GeodeCollection))]
public class GeodeContainerSmokeTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void ContainerStartsAndExposesEndpoints()
    {
        Assert.Equal(2, fx.LocatorPorts.Count);
        Assert.Equal(3, fx.ServerPorts.Count);
        Assert.All(fx.LocatorPorts, p => Assert.True(p > 0));
        Assert.All(fx.ServerPorts, p => Assert.True(p > 0));
    }

    [Fact]
    public async Task ClusterReportsTwoLocatorsAndThreeServers()
    {
        // `list members` is the cheapest end-to-end proof that the
        // 2-locator + 3-server topology actually assembled — not just
        // that ports opened. Without this assertion a silently-failed
        // server start (port collision, OOM) would only surface as
        // strange failures in downstream tests.
        using var cts = new CancellationTokenSource(TestTimeout);
        var members = await fx.GfshAsync("list members", cts.Token);

        Assert.Contains("loc1", members);
        Assert.Contains("loc2", members);
        Assert.Contains("srv1", members);
        Assert.Contains("srv2", members);
        Assert.Contains("srv3", members);
    }
}

*/