/*
using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.Cache;

public class CacheLibraryOptionsTests
{
    [Fact]
    public void Clone_copies_values()
    {
        var original = new CacheLibraryOptions
        {
            LibraryName = "mylib",
            LibraryFunctionName = "createCacheLoader",
        };
        var clone = original.Clone();

        Assert.Equal("mylib", clone.LibraryName);
        Assert.Equal("createCacheLoader", clone.LibraryFunctionName);
        Assert.IsType<CacheLibraryOptions>(clone);
    }

    [Fact]
    public void Clone_on_subclass_via_base_reference_returns_subtype()
    {
        // Polymorphic clone — slots typed as CacheLibraryOptions
        // (e.g. RegionAttributes.CacheLoader) may hold a
        // CachePersistenceManagerOptions instance; cloning must
        // preserve the runtime type.
        CacheLibraryOptions original = new CachePersistenceManagerOptions
        {
            LibraryName = "pm",
            LibraryFunctionName = "createPm",
            Properties = { ["disk-dir"] = "/var/cache" },
        };

        var clone = original.Clone();

        Assert.IsType<CachePersistenceManagerOptions>(clone);
        var pmClone = (CachePersistenceManagerOptions)clone;
        Assert.Equal("pm", pmClone.LibraryName);
        Assert.Equal("/var/cache", pmClone.Properties["disk-dir"]);
    }

    [Fact]
    public void Validate_no_rules()
    {
        Assert.Empty(new CacheLibraryOptions().Validate("lib"));
    }
}

*/