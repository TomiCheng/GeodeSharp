using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.CacheXml;

public class CacheXmlHostPortTests
{
    // ── DeepClone ─────────────────────────────────────────────────

    [Fact]
    public void DeepClone_copies_values()
    {
        var original = new CacheXmlHostPort { Host = "h", Port = 42 };
        var clone = original.DeepClone();

        Assert.Equal("h", clone.Host);
        Assert.Equal(42, clone.Port);
    }

    [Fact]
    public void DeepClone_mutating_clone_does_not_affect_original()
    {
        var original = new CacheXmlHostPort { Host = "h", Port = 42 };
        var clone = original.DeepClone();

        clone.Host = "mutated";
        clone.Port = 9999;

        Assert.Equal("h", original.Host);
        Assert.Equal(42, original.Port);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_valid_entry_passes()
    {
        Assert.Empty(new CacheXmlHostPort { Host = "h", Port = 1 }.Validate("hp"));
        Assert.Empty(new CacheXmlHostPort { Host = "h", Port = 65535 }.Validate("hp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_empty_or_whitespace_host_fails(string host)
    {
        var failures = new CacheXmlHostPort { Host = host, Port = 1 }.Validate("hp").ToList();
        Assert.Contains(failures, f => f.Contains("hp.Host"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void Validate_out_of_range_port_fails(int port)
    {
        var failures = new CacheXmlHostPort { Host = "h", Port = port }.Validate("hp").ToList();
        Assert.Contains(failures, f => f.Contains("hp.Port") && f.Contains(port.ToString()));
    }
}
