using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.CacheXml;

public class CacheXmlPersistenceManagerOptionsTests
{
    [Fact]
    public void DeepClone_copies_base_and_subclass_state()
    {
        var original = new CacheXmlPersistenceManagerOptions
        {
            LibraryName = "pm",
            LibraryFunctionName = "createPm",
            Properties = { ["disk-dir"] = "/var/cache", ["max-disk-size"] = "1G" },
        };
        var clone = original.DeepClone();

        Assert.Equal("pm", clone.LibraryName);
        Assert.Equal("createPm", clone.LibraryFunctionName);
        Assert.Equal("/var/cache", clone.Properties["disk-dir"]);
        Assert.Equal("1G", clone.Properties["max-disk-size"]);
    }

    [Fact]
    public void DeepClone_returns_subclass_type_via_covariant_return()
    {
        // Static type is the subclass — no cast needed.
        var original = new CacheXmlPersistenceManagerOptions();
        CacheXmlPersistenceManagerOptions clone = original.DeepClone();

        Assert.NotNull(clone);
    }

    [Fact]
    public void DeepClone_mutating_clone_dict_does_not_affect_original()
    {
        var original = new CacheXmlPersistenceManagerOptions
        {
            Properties = { ["k"] = "v" },
        };
        var clone = original.DeepClone();

        Assert.NotSame(original.Properties, clone.Properties);

        clone.Properties["k"] = "mutated";
        clone.Properties["k2"] = "added";

        Assert.Equal("v", original.Properties["k"]);
        Assert.False(original.Properties.ContainsKey("k2"));
    }

    [Fact]
    public void Validate_no_rules()
    {
        Assert.Empty(new CacheXmlPersistenceManagerOptions().Validate("pm"));
    }
}
