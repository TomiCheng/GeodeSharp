using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.CacheXml;

public class CacheXmlLibraryOptionsTests
{
    [Fact]
    public void DeepClone_copies_values()
    {
        var original = new CacheXmlLibraryOptions
        {
            LibraryName = "mylib",
            LibraryFunctionName = "createCacheLoader",
        };
        var clone = original.DeepClone();

        Assert.Equal("mylib", clone.LibraryName);
        Assert.Equal("createCacheLoader", clone.LibraryFunctionName);
        Assert.IsType<CacheXmlLibraryOptions>(clone);
    }

    [Fact]
    public void DeepClone_on_subclass_via_base_reference_returns_subtype()
    {
        // Polymorphic clone — slots typed as CacheXmlLibraryOptions
        // (e.g. RegionAttributes.CacheLoader) may hold a
        // CacheXmlPersistenceManagerOptions instance; cloning must
        // preserve the runtime type.
        CacheXmlLibraryOptions original = new CacheXmlPersistenceManagerOptions
        {
            LibraryName = "pm",
            LibraryFunctionName = "createPm",
            Properties = { ["disk-dir"] = "/var/cache" },
        };

        var clone = original.DeepClone();

        Assert.IsType<CacheXmlPersistenceManagerOptions>(clone);
        var pmClone = (CacheXmlPersistenceManagerOptions)clone;
        Assert.Equal("pm", pmClone.LibraryName);
        Assert.Equal("/var/cache", pmClone.Properties["disk-dir"]);
    }

    [Fact]
    public void Validate_no_rules()
    {
        Assert.Empty(new CacheXmlLibraryOptions().Validate("lib"));
    }
}
