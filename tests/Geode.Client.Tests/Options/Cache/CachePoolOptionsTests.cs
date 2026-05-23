/*
using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.Cache;

/// <summary>
/// Tests for <see cref="CachePoolOptions"/>:
/// <see cref="CachePoolOptions.Clone"/> (round-trip + mutation
/// isolation for the nested <c>Locators</c> / <c>Servers</c> lists)
/// and <see cref="CachePoolOptions.Validate"/> (Name, locators+servers
/// count, Min/Max connection bounds, recursion into HostPort entries).
/// </summary>
public class CachePoolOptionsTests
{
    private static CachePoolOptions MakeValidPool() => new()
    {
        Name = "p1",
        MinConnections = 2,
        MaxConnections = 8,
        Locators = { new CacheHostPortOptions { Host = "locator", Port = 10334 } },
        Servers = { new CacheHostPortOptions { Host = "server", Port = 40404 } },
    };

    // ── Clone ─────────────────────────────────────────────────

    [Fact]
    public void Clone_copies_all_primitive_values()
    {
        var original = MakeValidPool();
        original.IdleTimeout = TimeSpan.FromSeconds(42);
        original.ServerGroup = "group-a";
        original.SocketBufferSize = 4096;
        original.SubscriptionEnabled = true;

        var clone = original.Clone();

        Assert.Equal(original.Name, clone.Name);
        Assert.Equal(original.MinConnections, clone.MinConnections);
        Assert.Equal(original.MaxConnections, clone.MaxConnections);
        Assert.Equal(original.IdleTimeout, clone.IdleTimeout);
        Assert.Equal(original.ServerGroup, clone.ServerGroup);
        Assert.Equal(original.SocketBufferSize, clone.SocketBufferSize);
        Assert.Equal(original.SubscriptionEnabled, clone.SubscriptionEnabled);
    }

    [Fact]
    public void Clone_returns_different_list_instances()
    {
        var original = MakeValidPool();
        var clone = original.Clone();

        // Mutation-isolation precondition: lists are distinct references.
        Assert.NotSame(original.Locators, clone.Locators);
        Assert.NotSame(original.Servers, clone.Servers);
    }

    [Fact]
    public void Clone_mutating_clone_does_not_affect_original()
    {
        var original = MakeValidPool();
        var clone = original.Clone();

        clone.Locators.Add(new CacheHostPortOptions { Host = "new-locator", Port = 11111 });
        clone.Servers[0].Host = "mutated-server";
        clone.Name = "mutated-pool";

        Assert.Single(original.Locators);                  // not 2
        Assert.Equal("server", original.Servers[0].Host);   // not mutated
        Assert.Equal("p1", original.Name);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_default_valid_pool_passes()
    {
        var pool = MakeValidPool();
        Assert.Empty(pool.Validate("p"));
    }

    [Fact]
    public void Validate_empty_name_fails()
    {
        var pool = MakeValidPool();
        pool.Name = "";

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("p.Name"));
    }

    [Fact]
    public void Validate_whitespace_name_fails()
    {
        var pool = MakeValidPool();
        pool.Name = "   ";

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("p.Name"));
    }

    [Fact]
    public void Validate_no_locators_or_servers_fails()
    {
        var pool = MakeValidPool();
        pool.Locators.Clear();
        pool.Servers.Clear();

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("at least one locator or server"));
    }

    [Fact]
    public void Validate_one_locator_only_passes()
    {
        // "at least one" — one is enough, even with empty Servers.
        var pool = MakeValidPool();
        pool.Servers.Clear();

        Assert.Empty(pool.Validate("p"));
    }

    [Fact]
    public void Validate_negative_min_connections_fails()
    {
        var pool = MakeValidPool();
        pool.MinConnections = -1;

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("MinConnections") && f.Contains("-1"));
    }

    [Fact]
    public void Validate_zero_min_connections_passes()
    {
        // cppcache parity: 0 means "pure lazy" — allowed.
        var pool = MakeValidPool();
        pool.MinConnections = 0;

        Assert.Empty(pool.Validate("p"));
    }

    [Fact]
    public void Validate_max_below_min_fails()
    {
        var pool = MakeValidPool();
        pool.MinConnections = 5;
        pool.MaxConnections = 3;

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("MaxConnections") && f.Contains("MinConnections"));
    }

    [Fact]
    public void Validate_null_max_connections_passes()
    {
        // null = unbounded; comparison is skipped.
        var pool = MakeValidPool();
        pool.MaxConnections = null;

        Assert.Empty(pool.Validate("p"));
    }

    [Fact]
    public void Validate_bad_locator_propagates_with_indexed_path()
    {
        var pool = MakeValidPool();
        pool.Locators.Add(new CacheHostPortOptions { Host = "", Port = 99999 });  // both bad

        var failures = pool.Validate("p").ToList();
        Assert.Contains(failures, f => f.Contains("p.Locators[1].Host"));
        Assert.Contains(failures, f => f.Contains("p.Locators[1].Port"));
    }
}

*/