using Geode.Client.Options;
using Xunit;

namespace Geode.Client.Tests.Options;

/// <summary>
/// Consolidated tests for options classes that contain only primitives /
/// strings / enums / TimeSpan / nullables — no nested options or
/// collections, no validation rules. The shared pattern is:
/// <list type="number">
///   <item>Set a non-default value on each property.</item>
///   <item>Clone — assert each property round-trips.</item>
///   <item>Validate returns empty (parity stubs, no structural rules yet).</item>
/// </list>
/// Per-class tests live as nested classes for keeping the file
/// navigable but the assertions distinct per type.
/// </summary>
public class PrimitiveOptionsTests
{
    public class SubscriptionOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new SubscriptionOptions
            {
                DurableClientId = "client-1",
                DurableTimeout = TimeSpan.FromMinutes(10),
                AutoReadyForEvents = false,
                RedundancyMonitorInterval = TimeSpan.FromSeconds(5),
                NotifyAckInterval = TimeSpan.FromMilliseconds(500),
                NotifyDupCheckLife = TimeSpan.FromMinutes(2),
                ConflateEvents = true,
            };
            var clone = original.Clone();

            Assert.Equal("client-1", clone.DurableClientId);
            Assert.Equal(TimeSpan.FromMinutes(10), clone.DurableTimeout);
            Assert.False(clone.AutoReadyForEvents);
            Assert.Equal(TimeSpan.FromSeconds(5), clone.RedundancyMonitorInterval);
            Assert.Equal(TimeSpan.FromMilliseconds(500), clone.NotifyAckInterval);
            Assert.Equal(TimeSpan.FromMinutes(2), clone.NotifyDupCheckLife);
            Assert.Equal(true, clone.ConflateEvents);
        }

        [Fact]
        public void Clone_mutation_isolation()
        {
            var original = new SubscriptionOptions { DurableClientId = "a" };
            var clone = original.Clone();
            clone.DurableClientId = "mutated";
            Assert.Equal("a", original.DurableClientId);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new SubscriptionOptions().Validate("s"));
    }

    public class TlsOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new TlsOptions
            {
                Enabled = true,
                KeyStorePath = "/ks",
                KeyStorePassword = "pw",
                TrustStorePath = "/ts",
            };
            var clone = original.Clone();

            Assert.True(clone.Enabled);
            Assert.Equal("/ks", clone.KeyStorePath);
            Assert.Equal("pw", clone.KeyStorePassword);
            Assert.Equal("/ts", clone.TrustStorePath);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new TlsOptions().Validate("t"));
    }

    public class LogOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new LogOptions
            {
                Filename = "/log",
                Level = LogLevel.Debug,
                FileSizeLimit = 100,
                DiskSpaceLimit = 1000,
            };
            var clone = original.Clone();

            Assert.Equal("/log", clone.Filename);
            Assert.Equal(LogLevel.Debug, clone.Level);
            Assert.Equal(100u, clone.FileSizeLimit);
            Assert.Equal(1000u, clone.DiskSpaceLimit);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new LogOptions().Validate("l"));
    }

    public class StatisticsOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new StatisticsOptions
            {
                Enabled = true,
                SampleInterval = TimeSpan.FromSeconds(5),
                ArchiveFile = "custom.gfs",
                FileSizeLimit = 50,
                DiskSpaceLimit = 500,
                TimeStatisticsEnabled = true,
            };
            var clone = original.Clone();

            Assert.True(clone.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(5), clone.SampleInterval);
            Assert.Equal("custom.gfs", clone.ArchiveFile);
            Assert.Equal(50u, clone.FileSizeLimit);
            Assert.Equal(500u, clone.DiskSpaceLimit);
            Assert.True(clone.TimeStatisticsEnabled);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new StatisticsOptions().Validate("st"));
    }

    public class TxOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new TxOptions { SuspendedTimeout = TimeSpan.FromMinutes(2) };
            var clone = original.Clone();
            Assert.Equal(TimeSpan.FromMinutes(2), clone.SuspendedTimeout);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new TxOptions().Validate("tx"));
    }

    public class HeapOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new HeapOptions
            {
                LRULimit = 1024,
                LRUDelta = 20,
                TombstoneTimeout = TimeSpan.FromMinutes(8),
            };
            var clone = original.Clone();

            Assert.Equal(1024ul, clone.LRULimit);
            Assert.Equal(20, clone.LRUDelta);
            Assert.Equal(TimeSpan.FromMinutes(8), clone.TombstoneTimeout);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new HeapOptions().Validate("h"));
    }

    public class PdxOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new PdxOptions { ClearTypeIdsOnDisconnect = true };
            var clone = original.Clone();
            Assert.True(clone.ClearTypeIdsOnDisconnect);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new PdxOptions().Validate("p"));
    }

    public class PoolOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new PoolOptions
            {
                ConnectionPoolSize = 10,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ConnectWaitTimeout = TimeSpan.FromSeconds(5),
                MaxSocketBufferSize = 16384,
                PingInterval = TimeSpan.FromSeconds(20),
                ShuffleEndpoints = false,
                BucketWaitTimeout = TimeSpan.FromSeconds(2),
            };
            var clone = original.Clone();

            Assert.Equal(10, clone.ConnectionPoolSize);
            Assert.Equal(TimeSpan.FromSeconds(30), clone.ConnectTimeout);
            Assert.Equal(TimeSpan.FromSeconds(5), clone.ConnectWaitTimeout);
            Assert.Equal(16384, clone.MaxSocketBufferSize);
            Assert.Equal(TimeSpan.FromSeconds(20), clone.PingInterval);
            Assert.False(clone.ShuffleEndpoints);
            Assert.Equal(TimeSpan.FromSeconds(2), clone.BucketWaitTimeout);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new PoolOptions().Validate("p"));
    }

    public class CacheExpirationOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new CacheExpirationOptions
            {
                Timeout = TimeSpan.FromMinutes(15),
                Action = CacheExpirationAction.Invalidate,
            };
            var clone = original.Clone();

            Assert.Equal(TimeSpan.FromMinutes(15), clone.Timeout);
            Assert.Equal(CacheExpirationAction.Invalidate, clone.Action);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new CacheExpirationOptions().Validate("e"));
    }

    public class CachePdxOptionsTests
    {
        [Fact]
        public void Clone_round_trips()
        {
            var original = new CachePdxOptions
            {
                IgnoreUnreadFields = true,
                ReadSerialized = false,
            };
            var clone = original.Clone();

            Assert.Equal(true, clone.IgnoreUnreadFields);
            Assert.Equal(false, clone.ReadSerialized);
        }

        [Fact]
        public void Validate_empty() => Assert.Empty(new CachePdxOptions().Validate("px"));
    }
}
