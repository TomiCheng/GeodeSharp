using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options.CacheXml;

public class CacheXmlRegionAttributesOptionsTests
{
    [Fact]
    public void Clone_copies_primitives()
    {
        var original = new CacheXmlRegionAttributesOptions
        {
            CachingEnabled = true,
            CloningEnabled = false,
            Scope = CacheXmlScope.DistributedAck,
            InitialCapacity = 16,
            LoadFactor = 0.75f,
            ConcurrencyLevel = 4,
            LruEntriesLimit = 100,
            DiskPolicy = CacheXmlDiskPolicy.None,
            Endpoints = "host:port",
            ClientNotification = true,
            PoolName = "p1",
            ConcurrencyChecksEnabled = false,
            RefId = "ref",
        };
        var clone = original.Clone();

        Assert.Equal(true, clone.CachingEnabled);
        Assert.Equal(false, clone.CloningEnabled);
        Assert.Equal(CacheXmlScope.DistributedAck, clone.Scope);
        Assert.Equal(16, clone.InitialCapacity);
        Assert.Equal(0.75f, clone.LoadFactor);
        Assert.Equal("p1", clone.PoolName);
        Assert.Equal("ref", clone.RefId);
    }

    [Fact]
    public void Clone_recursively_clones_nullable_expiration_options()
    {
        var original = new CacheXmlRegionAttributesOptions
        {
            RegionTimeToLive = new CacheXmlExpirationOptions { Timeout = TimeSpan.FromMinutes(5) },
            EntryIdleTime = new CacheXmlExpirationOptions { Timeout = TimeSpan.FromMinutes(1) },
        };
        var clone = original.Clone();

        Assert.NotSame(original.RegionTimeToLive, clone.RegionTimeToLive);
        Assert.NotSame(original.EntryIdleTime, clone.EntryIdleTime);
        Assert.Equal(TimeSpan.FromMinutes(5), clone.RegionTimeToLive!.Timeout);
        Assert.Null(clone.RegionIdleTime);   // unset slots stay null
        Assert.Null(clone.EntryTimeToLive);
    }

    [Fact]
    public void Clone_polymorphically_clones_library_options_slots()
    {
        var original = new CacheXmlRegionAttributesOptions
        {
            CacheLoader = new CacheXmlLibraryOptions { LibraryName = "loader" },
            // Polymorphic — PersistenceManager IS a CacheXmlLibraryOptions slot via subclass.
            PersistenceManager = new CacheXmlPersistenceManagerOptions
            {
                LibraryName = "pm",
                Properties = { ["dir"] = "/data" },
            },
        };
        var clone = original.Clone();

        Assert.NotSame(original.CacheLoader, clone.CacheLoader);
        Assert.Equal("loader", clone.CacheLoader!.LibraryName);

        Assert.NotSame(original.PersistenceManager, clone.PersistenceManager);
        Assert.IsType<CacheXmlPersistenceManagerOptions>(clone.PersistenceManager);
        Assert.Equal("/data", clone.PersistenceManager!.Properties["dir"]);
    }

    [Fact]
    public void Clone_mutating_clone_nested_does_not_affect_original()
    {
        var original = new CacheXmlRegionAttributesOptions
        {
            RegionTimeToLive = new CacheXmlExpirationOptions { Timeout = TimeSpan.FromMinutes(5) },
            CacheLoader = new CacheXmlLibraryOptions { LibraryName = "loader" },
        };
        var clone = original.Clone();

        clone.RegionTimeToLive!.Timeout = TimeSpan.FromHours(1);
        clone.CacheLoader!.LibraryName = "mutated";

        Assert.Equal(TimeSpan.FromMinutes(5), original.RegionTimeToLive!.Timeout);
        Assert.Equal("loader", original.CacheLoader!.LibraryName);
    }

    [Fact]
    public void Validate_no_own_rules_delegates_to_nested()
    {
        // No structural rules on this class itself. With all-null nested,
        // nothing fails.
        Assert.Empty(new CacheXmlRegionAttributesOptions().Validate("attrs"));
    }
}
