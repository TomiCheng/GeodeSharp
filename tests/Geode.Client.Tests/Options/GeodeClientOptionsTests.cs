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
            Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "p1",
                        Servers = { new CacheHostPortOptions { Host = "localhost", Port = 40404 } },
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
        original.ThreadPoolSize = 16;
        original.EnableChunkHandlerThread = true;

        var clone = original.Clone();

        Assert.Equal("n", clone.Name);
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
        Assert.NotSame(original.Security, clone.Security);
        Assert.NotSame(original.Tx, clone.Tx);
        Assert.NotSame(original.Heap, clone.Heap);
        Assert.NotSame(original.Pdx, clone.Pdx);
        Assert.NotSame(original.Serialization, clone.Serialization);
        Assert.NotSame(original.Cache, clone.Cache);
    }

    [Fact]
    public void Clone_with_null_Cache_leaves_clone_null()
    {
        var original = new GeodeClientOptions();   // Cache defaults to null
        var clone = original.Clone();

        Assert.Null(clone.Cache);
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
        clone.Cache!.Pools[0].Name = "mutated-pool";

        Assert.Equal("test-client", original.Name);
        Assert.Equal(5, original.Pool.ConnectionPoolSize);
        Assert.Equal(64, original.Serialization.MaxDepth);
        Assert.Equal("alice", original.Security.Properties["user"]);
        Assert.Equal("p1", original.Cache!.Pools[0].Name);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_default_options_pass()
    {
        // Default GeodeClientOptions (Cache null, defaults everywhere)
        // has no failures — Cache=null is deliberately allowed.
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
        opts.Cache!.Pools.Clear();   // triggers "must set either Endpoints or Pools"

        var failures = opts.Validate("root").ToList();
        Assert.Contains(failures, f => f.Contains("root.Cache must set either Endpoints or Pools"));
    }

    [Fact]
    public void Validate_null_cachexml_skips_section()
    {
        // Cache=null is allowed — manual cache-creation path will
        // populate it via Create(action). No failures here.
        var opts = new GeodeClientOptions();
        Assert.Empty(opts.Validate("root"));
    }
}
