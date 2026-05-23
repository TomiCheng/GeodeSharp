using Geode.Client.Internal;
using Xunit;

namespace Geode.Client.Tests.Internal;

public class RegionAttributesTests
{
    // ── Defaults (lock cppcache RegionAttributes.cpp:43-58 parity) ──

    [Fact]
    public void Defaults_MatchCppcacheConstants()
    {
        var a = new RegionAttributes();

        Assert.Equal(10000, a.InitialCapacity);
        Assert.Equal(0.75f, a.LoadFactor);
        Assert.Equal(16, a.ConcurrencyLevel);
        Assert.Equal(0, a.LruEntriesLimit);
        Assert.True(a.CachingEnabled);
        Assert.False(a.CloningEnabled);
        Assert.True(a.ConcurrencyChecksEnabled);
        Assert.Equal(string.Empty, a.PoolName);
    }

    // ── Clone semantics ───────────────────────────────────────────

    [Fact]
    public void Clone_ReturnsNewInstance()
    {
        var a = new RegionAttributes();
        var b = a.Clone();

        Assert.NotSame(a, b);
    }

    [Fact]
    public void Clone_CopiesAllFields()
    {
        var a = new RegionAttributes
        {
            InitialCapacity = 42,
            LoadFactor = 0.5f,
            ConcurrencyLevel = 8,
            LruEntriesLimit = 100,
            CachingEnabled = false,
            CloningEnabled = true,
            ConcurrencyChecksEnabled = false,
            PoolName = "p",
        };

        var b = a.Clone();

        Assert.Equal(42, b.InitialCapacity);
        Assert.Equal(0.5f, b.LoadFactor);
        Assert.Equal(8, b.ConcurrencyLevel);
        Assert.Equal(100, b.LruEntriesLimit);
        Assert.False(b.CachingEnabled);
        Assert.True(b.CloningEnabled);
        Assert.False(b.ConcurrencyChecksEnabled);
        Assert.Equal("p", b.PoolName);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var a = new RegionAttributes { PoolName = "p", InitialCapacity = 42 };
        var b = a.Clone();

        b.PoolName = "q";
        b.InitialCapacity = 99;

        // Mutations on the clone don't leak back.
        Assert.Equal("p", a.PoolName);
        Assert.Equal(42, a.InitialCapacity);
    }
}
