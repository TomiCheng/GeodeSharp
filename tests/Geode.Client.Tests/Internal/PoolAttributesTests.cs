using Geode.Client.Internal;
using Xunit;

namespace Geode.Client.Tests.Internal;

public class PoolAttributesTests
{
    // ── Defaults (lock cppcache PoolFactory::DEFAULT_* parity) ────

    [Fact]
    public void Defaults_MatchCppcacheConstants()
    {
        var a = new PoolAttributes();

        Assert.Equal(TimeSpan.FromSeconds(10), a.FreeConnectionTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), a.LoadConditioningInterval);
        Assert.Equal(32768, a.SocketBufferSize);
        Assert.Equal(TimeSpan.FromSeconds(10), a.ReadTimeout);
        Assert.Equal(1, a.MinConnections);
        Assert.Equal(-1, a.MaxConnections);
        Assert.Equal(TimeSpan.FromSeconds(5), a.IdleTimeout);
        Assert.Equal(-1, a.RetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), a.PingInterval);
        Assert.Equal(TimeSpan.FromSeconds(5), a.UpdateLocatorListInterval);
        Assert.Equal(TimeSpan.Zero, a.StatisticInterval);
        Assert.False(a.SubscriptionEnabled);
        Assert.Equal(0, a.SubscriptionRedundancy);
        Assert.Equal(TimeSpan.FromSeconds(900), a.SubscriptionMessageTrackingTimeout);
        Assert.Equal(TimeSpan.FromSeconds(100), a.SubscriptionAckInterval);
        Assert.False(a.ThreadLocalConnection);
        Assert.False(a.MultiuserSecureMode);
        Assert.True(a.PrSingleHopEnabled);
        Assert.Equal(string.Empty, a.ServerGroup);
        Assert.Equal(string.Empty, a.SniProxyHost);
        Assert.Equal(0, a.SniProxyPort);
        Assert.Empty(a.Locators);
        Assert.Empty(a.Servers);
    }

    // ── AddLocator / AddServer mutual exclusion ───────────────────

    [Fact]
    public void AddLocator_AfterAddServer_ThrowsArgumentException()
    {
        var a = new PoolAttributes();
        a.AddServer("h", 40404);

        Assert.Throws<ArgumentException>(() => a.AddLocator("h", 10334));
    }

    [Fact]
    public void AddServer_AfterAddLocator_ThrowsArgumentException()
    {
        var a = new PoolAttributes();
        a.AddLocator("h", 10334);

        Assert.Throws<ArgumentException>(() => a.AddServer("h", 40404));
    }

    // ── Clone semantics ───────────────────────────────────────────

    [Fact]
    public void Clone_CopiesAllScalarFields()
    {
        var a = new PoolAttributes
        {
            FreeConnectionTimeout = TimeSpan.FromSeconds(7),
            MinConnections = 3,
            MaxConnections = 50,
            ServerGroup = "g1",
            SubscriptionEnabled = true,
            PrSingleHopEnabled = false,
            SniProxyHost = "sni",
            SniProxyPort = 8443,
        };

        var c = a.Clone();

        Assert.Equal(TimeSpan.FromSeconds(7), c.FreeConnectionTimeout);
        Assert.Equal(3, c.MinConnections);
        Assert.Equal(50, c.MaxConnections);
        Assert.Equal("g1", c.ServerGroup);
        Assert.True(c.SubscriptionEnabled);
        Assert.False(c.PrSingleHopEnabled);
        Assert.Equal("sni", c.SniProxyHost);
        Assert.Equal(8443, c.SniProxyPort);
    }

    [Fact]
    public void Clone_ScalarMutationOnCloneDoesNotAffectOriginal()
    {
        var a = new PoolAttributes { MinConnections = 1 };
        var c = a.Clone();

        c.MinConnections = 99;

        Assert.Equal(1, a.MinConnections);
        Assert.Equal(99, c.MinConnections);
    }

    [Fact]
    public void Clone_LocatorsListIsIndependent()
    {
        var a = new PoolAttributes();
        a.AddLocator("loc1", 10334);
        var c = a.Clone();

        c.AddLocator("loc2", 10335);

        Assert.Single(a.Locators);
        Assert.Equal(2, c.Locators.Count);
        Assert.Equal("loc1", a.Locators[0].Host);
    }

    [Fact]
    public void Clone_ServersListIsIndependent()
    {
        var a = new PoolAttributes();
        a.AddServer("srv1", 40404);
        var c = a.Clone();

        c.AddServer("srv2", 40405);

        Assert.Single(a.Servers);
        Assert.Equal(2, c.Servers.Count);
        Assert.Equal("srv1", a.Servers[0].Host);
    }

    // ── Validate ──────────────────────────────────────────────────

    [Fact]
    public void Validate_NoEndpoints_YieldsError()
    {
        var errors = new PoolAttributes().Validate("X").ToList();

        Assert.Contains(errors, e => e.Contains("at least one locator or server"));
    }

    [Fact]
    public void Validate_WithValidServer_NoErrors()
    {
        var a = new PoolAttributes();
        a.AddServer("h", 40404);

        Assert.Empty(a.Validate("X"));
    }

    [Fact]
    public void Validate_MaxUnboundedSentinel_IsNotErrored()
    {
        // -1 = unbounded; the MaxConnections < MinConnections rule must skip it.
        var a = new PoolAttributes { MinConnections = 10, MaxConnections = -1 };
        a.AddServer("h", 40404);

        Assert.Empty(a.Validate("X"));
    }

    [Fact]
    public void Validate_RetryAttemptsNegativeOneSentinel_IsNotErrored()
    {
        // -1 = pool decides (cppcache DEFAULT_RETRY_ATTEMPTS).
        var a = new PoolAttributes { RetryAttempts = -1 };
        a.AddServer("h", 40404);

        Assert.Empty(a.Validate("X"));
    }
}
