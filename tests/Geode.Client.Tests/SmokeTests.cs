using FluentAssertions;
using Xunit;

namespace Geode.Client.Tests;

public class SmokeTests
{
    [Fact]
    public void TestInfrastructureWorks()
    {
        // Phase 0 sanity check. Replace once Phase 1 codec tests are added.
        (1 + 1).Should().Be(2);
    }
}
