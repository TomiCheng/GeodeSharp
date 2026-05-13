using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options;

public class SecurityOptionsTests
{
    [Fact]
    public void DeepClone_copies_strings_and_dictionary_entries()
    {
        var original = new SecurityOptions
        {
            ClientDhAlgo = "DH",
            ClientKsPath = "/path",
            Properties = { ["user"] = "alice", ["password"] = "s3cret" },
        };
        var clone = original.DeepClone();

        Assert.Equal("DH", clone.ClientDhAlgo);
        Assert.Equal("/path", clone.ClientKsPath);
        Assert.Equal("alice", clone.Properties["user"]);
        Assert.Equal("s3cret", clone.Properties["password"]);
    }

    [Fact]
    public void DeepClone_returns_different_dictionary_instance()
    {
        var original = new SecurityOptions { Properties = { ["k"] = "v" } };
        var clone = original.DeepClone();

        Assert.NotSame(original.Properties, clone.Properties);
    }

    [Fact]
    public void DeepClone_mutating_clone_does_not_affect_original()
    {
        var original = new SecurityOptions { Properties = { ["k"] = "v" } };
        var clone = original.DeepClone();

        clone.Properties["k"] = "mutated";
        clone.Properties.Add("k2", "v2");

        Assert.Equal("v", original.Properties["k"]);
        Assert.False(original.Properties.ContainsKey("k2"));
    }

    [Fact]
    public void Validate_no_rules()
    {
        Assert.Empty(new SecurityOptions().Validate("sec"));
    }
}
