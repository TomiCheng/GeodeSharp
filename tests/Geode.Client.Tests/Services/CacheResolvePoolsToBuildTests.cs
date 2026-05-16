using Geode.Client.Options;
using Geode.Client.Services;
using Xunit;

namespace Geode.Client.Tests.Services;

/// <summary>
/// Pure-function tests for <see cref="Cache.ResolvePoolsToBuild"/> — the
/// projection from <see cref="CacheOptions"/> to the list of pools the
/// cache builds at init. Covers Endpoints→default-pool synthesis,
/// Pools pass-through, and "don't mutate the input options" guarantee.
/// </summary>
public class CacheResolvePoolsToBuildTests
{
    [Fact]
    public void Pools_only_returns_pools_unchanged()
    {
        var cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "p1",
                    Servers = { new CacheHostPortOptions { Host = "h", Port = 40404 } },
                },
            },
        };

        var resolved = Cache.ResolvePoolsToBuild(cache);

        Assert.Same(cache.Pools, resolved);
    }

    [Fact]
    public void Endpoints_only_synthesises_single_default_pool()
    {
        var cache = new CacheOptions
        {
            Endpoints =
            {
                new CacheHostPortOptions { Host = "h1", Port = 40404 },
                new CacheHostPortOptions { Host = "h2", Port = 40405 },
            },
        };

        var resolved = Cache.ResolvePoolsToBuild(cache);

        Assert.Single(resolved);
        Assert.Equal("default", resolved[0].Name);
        Assert.Equal(2, resolved[0].Servers.Count);
        Assert.Equal("h1", resolved[0].Servers[0].Host);
        Assert.Equal(40404, resolved[0].Servers[0].Port);
        Assert.Equal("h2", resolved[0].Servers[1].Host);
        Assert.Equal(40405, resolved[0].Servers[1].Port);
    }

    [Fact]
    public void Endpoints_only_uses_CachePoolOptions_defaults_for_other_attributes()
    {
        var cache = new CacheOptions
        {
            Endpoints = { new CacheHostPortOptions { Host = "h", Port = 40404 } },
        };

        var pool = Cache.ResolvePoolsToBuild(cache).Single();

        // Spot-check that we did NOT cherry-pick from the global
        // PoolOptions or invent values — the synthesised pool's
        // tunables are CachePoolOptions defaults.
        var defaults = new CachePoolOptions();
        Assert.Equal(defaults.MinConnections, pool.MinConnections);
        Assert.Equal(defaults.IdleTimeout, pool.IdleTimeout);
        Assert.Equal(defaults.ServerGroup, pool.ServerGroup);
        Assert.Empty(pool.Locators);
    }

    [Fact]
    public void Endpoints_only_does_not_mutate_input_options()
    {
        var cache = new CacheOptions
        {
            Endpoints = { new CacheHostPortOptions { Host = "h", Port = 40404 } },
        };
        var endpointsBefore = cache.Endpoints.Count;
        var poolsBefore = cache.Pools.Count;

        Cache.ResolvePoolsToBuild(cache);

        Assert.Equal(endpointsBefore, cache.Endpoints.Count);
        Assert.Equal(poolsBefore, cache.Pools.Count);
    }

    [Fact]
    public void Endpoints_only_deep_clones_host_port_instances()
    {
        var ep = new CacheHostPortOptions { Host = "h", Port = 40404 };
        var cache = new CacheOptions { Endpoints = { ep } };

        var pool = Cache.ResolvePoolsToBuild(cache).Single();

        Assert.NotSame(ep, pool.Servers[0]);

        // Mutating the synthesised pool's server must not leak back
        // to the original endpoint entry.
        pool.Servers[0].Host = "mutated";
        Assert.Equal("h", ep.Host);
    }

    [Fact]
    public void Both_empty_returns_empty_pools()
    {
        // Validator catches this as a failure upstream, but the
        // resolver is a pure function and shouldn't crash — it just
        // returns the empty Pools list as-is.
        var cache = new CacheOptions();

        var resolved = Cache.ResolvePoolsToBuild(cache);

        Assert.Empty(resolved);
        Assert.Same(cache.Pools, resolved);
    }
}
