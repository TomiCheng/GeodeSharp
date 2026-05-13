using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.CacheXml;

public class CacheXmlOptionsTests
{
    private static CacheXmlOptions MakeValid()
    {
        return new CacheXmlOptions
        {
            Pools =
            {
                new CacheXmlPoolOptions
                {
                    Name = "p1",
                    Locators = { new CacheXmlHostPort { Host = "locator", Port = 10334 } },
                },
            },
        };
    }

    // ── DeepClone ─────────────────────────────────────────────────

    [Fact]
    public void DeepClone_copies_primitive_attributes()
    {
        var original = MakeValid();
        original.Endpoints = "ep";
        original.RedundancyLevel = "1";
        original.Version = "1.0";

        var clone = original.DeepClone();

        Assert.Equal("ep", clone.Endpoints);
        Assert.Equal("1", clone.RedundancyLevel);
        Assert.Equal("1.0", clone.Version);
    }

    [Fact]
    public void DeepClone_creates_independent_collections()
    {
        var original = MakeValid();
        original.Regions.Add(new CacheXmlRegionOptions { Name = "r" });
        original.NamedAttributes["tmpl"] = new CacheXmlRegionAttributesOptions { PoolName = "p1" };

        var clone = original.DeepClone();

        Assert.NotSame(original.Pools, clone.Pools);
        Assert.NotSame(original.Regions, clone.Regions);
        Assert.NotSame(original.NamedAttributes, clone.NamedAttributes);
        Assert.NotSame(original.Pdx, clone.Pdx);

        // Also: contained items are distinct instances
        Assert.NotSame(original.Pools[0], clone.Pools[0]);
        Assert.NotSame(original.NamedAttributes["tmpl"], clone.NamedAttributes["tmpl"]);
    }

    [Fact]
    public void DeepClone_mutating_clone_does_not_affect_original()
    {
        var original = MakeValid();
        original.Regions.Add(new CacheXmlRegionOptions { Name = "r" });
        original.NamedAttributes["tmpl"] = new CacheXmlRegionAttributesOptions { PoolName = "p1" };

        var clone = original.DeepClone();

        clone.Pools.Add(new CacheXmlPoolOptions { Name = "p2" });
        clone.Pools[0].Name = "mutated";
        clone.Regions[0].Name = "mutated-region";
        clone.NamedAttributes["tmpl"].PoolName = "mutated-pool";

        Assert.Single(original.Pools);
        Assert.Equal("p1", original.Pools[0].Name);
        Assert.Equal("r", original.Regions[0].Name);
        Assert.Equal("p1", original.NamedAttributes["tmpl"].PoolName);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_default_valid_options_pass()
    {
        Assert.Empty(MakeValid().Validate("cx"));
    }

    [Fact]
    public void Validate_empty_pools_fails()
    {
        var opts = MakeValid();
        opts.Pools.Clear();

        var failures = opts.Validate("cx").ToList();
        Assert.Contains(failures, f => f.Contains("cx.Pools must contain at least one pool"));
    }

    [Fact]
    public void Validate_pool_failures_propagate_with_indexed_path()
    {
        var opts = MakeValid();
        opts.Pools[0].Name = "";  // bad

        var failures = opts.Validate("cx").ToList();
        Assert.Contains(failures, f => f.Contains("cx.Pools[0].Name"));
    }

    [Fact]
    public void Validate_region_refid_unmatched_fails()
    {
        var opts = MakeValid();
        opts.Regions.Add(new CacheXmlRegionOptions { Name = "r", RefId = "missing-template" });
        // No NamedAttributes entry — refid dangling.

        var failures = opts.Validate("cx").ToList();
        Assert.Contains(failures, f => f.Contains("cx.Regions[0].RefId='missing-template'"));
    }

    [Fact]
    public void Validate_region_refid_matched_passes()
    {
        var opts = MakeValid();
        opts.NamedAttributes["tmpl"] = new CacheXmlRegionAttributesOptions { PoolName = "p1" };
        opts.Regions.Add(new CacheXmlRegionOptions { Name = "r", RefId = "tmpl" });

        Assert.Empty(opts.Validate("cx"));
    }

    [Fact]
    public void Validate_empty_refid_skips_cross_ref_check()
    {
        // RefId = "" means "no template" — no cross-ref to satisfy.
        var opts = MakeValid();
        opts.Regions.Add(new CacheXmlRegionOptions { Name = "r", RefId = "" });

        Assert.Empty(opts.Validate("cx"));
    }
}
