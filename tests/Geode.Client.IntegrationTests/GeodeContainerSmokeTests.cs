using FluentAssertions;
using Xunit;

namespace Geode.Client.IntegrationTests;

[Collection(nameof(GeodeCollection))]
public class GeodeContainerSmokeTests(GeodeFixture fx)
{
    [Fact]
    public void ContainerStartsAndExposesEndpoints()
    {
        // Phase 0: only verifies Testcontainers + Geode image work in this env.
        // Replace once Phase 2 (Ping) brings real client connectivity.
        fx.LocatorPort.Should().BeGreaterThan(0);
        fx.ServerPort.Should().BeGreaterThan(0);
    }
}
