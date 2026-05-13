using Geode.Client.Internal;
using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Internal;

/// <summary>
/// Direct unit tests for <see cref="GeodeClientOptionsValidator"/>
/// rules. Most validator behaviour is exercised end-to-end via
/// <see cref="Microsoft.Extensions.Options.ValidateOnStart"/> from
/// <c>GeodeClientExtensionsTests</c>; this file pins the per-rule
/// failure surface so each check can be verified in isolation as new
/// rules are added.
/// </summary>
public class GeodeClientOptionsValidatorTests
{
    /// <summary>
    /// Minimum options that pass every other rule — used as the
    /// baseline for rule-specific failure tests. Without this, a
    /// failing rule could be hidden by an unrelated rule failing first.
    /// </summary>
    private static GeodeClientOptions MinimalValidOptions()
    {
        return new GeodeClientOptions
        {
            CacheXml = new CacheXmlOptions
            {
                Pools =
                {
                    new CacheXmlPoolOptions
                    {
                        Name = "p1",
                        Servers = { new CacheXmlHostPort { Host = "localhost", Port = 40404 } },
                    },
                },
            },
        };
    }

    // ── Serialization.MaxDepth ─────────────────────────────────

    [Fact]
    public void MaxDepth_zero_fails()
    {
        var v = new GeodeClientOptionsValidator();
        var opts = MinimalValidOptions();
        opts.Serialization.MaxDepth = 0;

        var result = v.Validate(name: null, opts);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, f => f.Contains("Serialization.MaxDepth"));
    }

    [Fact]
    public void MaxDepth_negative_fails()
    {
        var v = new GeodeClientOptionsValidator();
        var opts = MinimalValidOptions();
        opts.Serialization.MaxDepth = -1;

        var result = v.Validate(name: null, opts);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, f => f.Contains("Serialization.MaxDepth"));
    }

    [Fact]
    public void MaxDepth_one_passes()
    {
        // Boundary: 1 is the minimum legal value. The runtime impact
        // (only top-level scalars work) is the caller's concern.
        var v = new GeodeClientOptionsValidator();
        var opts = MinimalValidOptions();
        opts.Serialization.MaxDepth = 1;

        var result = v.Validate(name: null, opts);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MaxDepth_default_passes()
    {
        // Default (64) is set in the SerializationOptions ctor and
        // must satisfy the validator out of the box, otherwise the
        // host build path breaks for every consumer.
        var v = new GeodeClientOptionsValidator();
        var opts = MinimalValidOptions();
        // Don't touch opts.Serialization — exercise the default.

        var result = v.Validate(name: null, opts);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MaxDepth_failure_includes_the_actual_bad_value()
    {
        // Failure message should help debugging — quote the configured
        // value back at the user so they spot the typo.
        var v = new GeodeClientOptionsValidator();
        var opts = MinimalValidOptions();
        opts.Serialization.MaxDepth = -7;

        var result = v.Validate(name: null, opts);

        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, f => f.Contains("-7"));
    }
}
