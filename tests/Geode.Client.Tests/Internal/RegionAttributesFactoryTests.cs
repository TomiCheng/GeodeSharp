using Geode.Client.Internal;
using Xunit;

namespace Geode.Client.Tests.Internal;

public class RegionAttributesFactoryTests
{
    // ── Ctors ─────────────────────────────────────────────────────

    [Fact]
    public void DefaultCtor_CreateReturnsDefaults()
    {
        var raf = new RegionAttributesFactory();

        var a = raf.Create();

        Assert.Equal(10000, a.InitialCapacity);
        Assert.True(a.CachingEnabled);
        Assert.Equal(string.Empty, a.PoolName);
    }

    [Fact]
    public void SeedCtor_NullSeed_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RegionAttributesFactory(null!));
    }

    [Fact]
    public void SeedCtor_CopiesFromSeed_IndependentOfSeedMutation()
    {
        var seed = new RegionAttributes { PoolName = "seedPool", InitialCapacity = 42 };
        var raf = new RegionAttributesFactory(seed);

        // Mutate seed after construction — the factory must hold its own copy.
        seed.PoolName = "leaked";
        seed.InitialCapacity = 0;

        var a = raf.Create();
        Assert.Equal("seedPool", a.PoolName);
        Assert.Equal(42, a.InitialCapacity);
    }

    // ── Fluent setters ────────────────────────────────────────────

    [Fact]
    public void Setters_ReturnSameFactoryInstance()
    {
        var raf = new RegionAttributesFactory();

        Assert.Same(raf, raf.SetPoolName("p"));
        Assert.Same(raf, raf.SetInitialCapacity(64));
        Assert.Same(raf, raf.SetLoadFactor(0.5f));
        Assert.Same(raf, raf.SetConcurrencyLevel(8));
        Assert.Same(raf, raf.SetLruEntriesLimit(100));
        Assert.Same(raf, raf.SetCachingEnabled(false));
        Assert.Same(raf, raf.SetCloningEnabled(true));
        Assert.Same(raf, raf.SetConcurrencyChecksEnabled(false));
    }

    [Fact]
    public void Setters_PropagateToCreatedAttributes()
    {
        var a = new RegionAttributesFactory()
            .SetPoolName("p")
            .SetInitialCapacity(64)
            .SetLoadFactor(0.5f)
            .SetConcurrencyLevel(8)
            .SetLruEntriesLimit(100)
            .SetCachingEnabled(false)
            .SetCloningEnabled(true)
            .SetConcurrencyChecksEnabled(false)
            .Create();

        Assert.Equal("p", a.PoolName);
        Assert.Equal(64, a.InitialCapacity);
        Assert.Equal(0.5f, a.LoadFactor);
        Assert.Equal(8, a.ConcurrencyLevel);
        Assert.Equal(100, a.LruEntriesLimit);
        Assert.False(a.CachingEnabled);
        Assert.True(a.CloningEnabled);
        Assert.False(a.ConcurrencyChecksEnabled);
    }

    // ── Create() snapshot semantics ───────────────────────────────

    [Fact]
    public void Create_ReturnsSnapshot_NotLiveAttributes()
    {
        var raf = new RegionAttributesFactory().SetPoolName("p");
        var snapshot = raf.Create();

        // Mutating the factory after Create() must not affect the snapshot.
        raf.SetPoolName("changed");
        var second = raf.Create();

        Assert.Equal("p", snapshot.PoolName);
        Assert.Equal("changed", second.PoolName);
        Assert.NotSame(snapshot, second);
    }
}
