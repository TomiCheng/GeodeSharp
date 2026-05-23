/*
using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.Cache;

public class CacheRegionOptionsTests
{
    private static CacheRegionOptions MakeRegion(string name = "r")
    {
        return new CacheRegionOptions
        {
            Name = name,
            Attributes = { PoolName = "p1" },
        };
    }

    // ── Clone ─────────────────────────────────────────────────

    [Fact]
    public void Clone_copies_name_and_refid()
    {
        var original = MakeRegion("r1");
        original.RefId = "tmpl";
        var clone = original.Clone();

        Assert.Equal("r1", clone.Name);
        Assert.Equal("tmpl", clone.RefId);
    }

    [Fact]
    public void Clone_returns_different_attributes_instance()
    {
        var original = MakeRegion();
        var clone = original.Clone();
        Assert.NotSame(original.Attributes, clone.Attributes);
    }

    [Fact]
    public void Clone_mutating_clone_attributes_does_not_affect_original()
    {
        var original = MakeRegion();
        var clone = original.Clone();

        clone.Attributes.PoolName = "mutated";

        Assert.Equal("p1", original.Attributes.PoolName);
    }

    [Fact]
    public void Clone_recursively_clones_child_regions()
    {
        var original = MakeRegion("parent");
        original.ChildRegions.Add(MakeRegion("child-1"));
        original.ChildRegions.Add(MakeRegion("child-2"));

        var clone = original.Clone();

        Assert.Equal(2, clone.ChildRegions.Count);
        Assert.Equal("child-1", clone.ChildRegions[0].Name);
        Assert.NotSame(original.ChildRegions[0], clone.ChildRegions[0]);
        Assert.NotSame(original.ChildRegions[0].Attributes, clone.ChildRegions[0].Attributes);

        // Mutate the clone's child
        clone.ChildRegions[0].Name = "mutated";
        Assert.Equal("child-1", original.ChildRegions[0].Name);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_default_passes()
    {
        Assert.Empty(MakeRegion().Validate("r"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_empty_or_whitespace_name_fails(string name)
    {
        var region = MakeRegion();
        region.Name = name;

        var failures = region.Validate("r").ToList();
        Assert.Contains(failures, f => f.Contains("r.Name"));
    }

    [Fact]
    public void Validate_recurses_into_child_regions_with_indexed_path()
    {
        var region = MakeRegion();
        region.ChildRegions.Add(new CacheRegionOptions { Name = "" });  // bad child

        var failures = region.Validate("r").ToList();
        Assert.Contains(failures, f => f.Contains("r.ChildRegions[0].Name"));
    }
}

*/