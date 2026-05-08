using Xunit;

namespace Geode.Client.Tests;

public class SmokeTests
{
    [Fact]
    public void TestInfrastructureWorks()
    {
        // Phase 0 sanity check. Replace once Phase 1 codec tests are added.
        Assert.Equal(2, 1 + 1);
    }
}
