using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options;

public class GeodeClientOptionsTests
{
    private static GeodeClientOptions MakeValid()
    {
        return new GeodeClientOptions
        {
            Name = "test-client",
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

    // ── Clone ─────────────────────────────────────────────────

    [Fact]
    public void Clone_copies_root_primitives()
    {
        var original = MakeValid();
        original.Name = "n";
        original.CacheXmlFile = "/file";
        original.ThreadPoolSize = 16;
        original.EnableChunkHandlerThread = true;

        var clone = original.Clone();

        Assert.Equal("n", clone.Name);
        Assert.Equal("/file", clone.CacheXmlFile);
        Assert.Equal(16u, clone.ThreadPoolSize);
        Assert.True(clone.EnableChunkHandlerThread);
    }

    [Fact]
    public void Clone_creates_independent_sub_options()
    {
        var original = MakeValid();
        var clone = original.Clone();

        // Every sub-options is a distinct instance.
        Assert.NotSame(original.Pool, clone.Pool);
        Assert.NotSame(original.Tls, clone.Tls);
        Assert.NotSame(original.Subscription, clone.Subscription);
        Assert.NotSame(original.Log, clone.Log);
        Assert.NotSame(original.Statistics, clone.Statistics);
        Assert.NotSame(original.Security, clone.Security);
        Assert.NotSame(original.Tx, clone.Tx);
        Assert.NotSame(original.Heap, clone.Heap);
        Assert.NotSame(original.Pdx, clone.Pdx);
        Assert.NotSame(original.Serialization, clone.Serialization);
        Assert.NotSame(original.CacheXml, clone.CacheXml);
    }

    [Fact]
    public void Clone_with_null_CacheXml_leaves_clone_null()
    {
        var original = new GeodeClientOptions();   // CacheXml defaults to null
        var clone = original.Clone();

        Assert.Null(clone.CacheXml);
    }

    [Fact]
    public void Clone_mutating_clone_does_not_affect_original()
    {
        var original = MakeValid();
        original.Security.Properties["user"] = "alice";
        var clone = original.Clone();

        clone.Name = "mutated";
        clone.Pool.ConnectionPoolSize = 99;
        clone.Serialization.MaxDepth = 999;
        clone.Security.Properties["user"] = "mutated";
        clone.CacheXml!.Pools[0].Name = "mutated-pool";

        Assert.Equal("test-client", original.Name);
        Assert.Equal(5, original.Pool.ConnectionPoolSize);
        Assert.Equal(64, original.Serialization.MaxDepth);
        Assert.Equal("alice", original.Security.Properties["user"]);
        Assert.Equal("p1", original.CacheXml!.Pools[0].Name);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_default_options_pass()
    {
        // Default GeodeClientOptions (CacheXml null, defaults everywhere)
        // has no failures — CacheXml=null is deliberately allowed.
        Assert.Empty(new GeodeClientOptions().Validate("root"));
    }

    [Fact]
    public void Validate_propagates_serialization_failures()
    {
        var opts = MakeValid();
        opts.Serialization.MaxDepth = 0;

        var failures = opts.Validate("root").ToList();
        Assert.Contains(failures, f => f.Contains("root.Serialization.MaxDepth"));
    }

    [Fact]
    public void Validate_propagates_cachexml_failures()
    {
        var opts = MakeValid();
        opts.CacheXml!.Pools.Clear();   // triggers "at least one pool"

        var failures = opts.Validate("root").ToList();
        Assert.Contains(failures, f => f.Contains("root.CacheXml.Pools"));
    }

    [Fact]
    public void Validate_null_cachexml_skips_section()
    {
        // CacheXml=null is allowed — manual cache-creation path will
        // populate it via Create(action). No failures here.
        var opts = new GeodeClientOptions();
        Assert.Empty(opts.Validate("root"));
    }
}
