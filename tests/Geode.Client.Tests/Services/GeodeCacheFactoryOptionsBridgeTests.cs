using Geode.Client;
using Geode.Client.Internal;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

/// <summary>
/// Locks the <c>GeodeClientOptions → SystemProperties</c> bridge:
/// values set inside <see cref="IGeodeCacheFactory.CreateAsync(string, Action{GeodeClientOptions, IServiceProvider}, CancellationToken)"/>
/// callback land on <c>cache.CacheProperties</c> at build time and stay
/// snapshot-stable afterwards.
/// </summary>
public class GeodeCacheFactoryOptionsBridgeTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    /// <summary>Resolve internal CacheProperties via the InternalsVisibleTo seam.</summary>
    private static SystemProperties Props(IGeodeCache cache) => ((GeodeCache)cache).CacheProperties;

    // ── No-configure paths use defaults ───────────────────────────

    [Fact]
    public async Task TwoArgOverload_UsesDefaults()
    {
        await using var sp = BuildSp();
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, Props(cache).Name);
        Assert.Equal(1_000_000, Props(cache).MaxArrayLength);
    }

    [Fact]
    public async Task ThreeArgOverload_NullConfigure_UsesDefaults()
    {
        await using var sp = BuildSp();
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", configure: null, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, Props(cache).Name);
        Assert.Equal(1_000_000, Props(cache).MaxArrayLength);
    }

    // ── Callback invocation contract ──────────────────────────────

    [Fact]
    public async Task Configure_Invoked_Once()
    {
        var calls = 0;
        await using var sp = BuildSp();
        await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", (_, _) => calls++, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Configure_ReceivesFactoryServiceProvider()
    {
        IServiceProvider? captured = null;
        await using var sp = BuildSp();
        await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", (_, csp) => captured = csp, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        // Verify caller can pull real services through the captured SP
        // (the whole point of the IServiceProvider parameter).
        Assert.NotNull(captured!.GetRequiredService<ILoggerFactory>());
    }

    // ── Top-level mapping ─────────────────────────────────────────

    [Fact]
    public async Task Mapping_Name()
    {
        var cache = await CreateWith(o => o.Name = "my-client");
        Assert.Equal("my-client", Props(cache).Name);
    }

    [Fact]
    public async Task Mapping_ThreadPoolSize()
    {
        var cache = await CreateWith(o => o.ThreadPoolSize = 42);
        Assert.Equal(42u, Props(cache).ThreadPoolSize);
    }

    // ── Subscription ──────────────────────────────────────────────

    [Fact]
    public async Task Mapping_Subscription_DurableClientId()
    {
        var cache = await CreateWith(o => o.Subscription.DurableClientId = "client-42");
        Assert.Equal("client-42", Props(cache).DurableClientId);
    }

    [Fact]
    public async Task Mapping_Subscription_DurableTimeout()
    {
        var cache = await CreateWith(o => o.Subscription.DurableTimeout = TimeSpan.FromMinutes(7));
        Assert.Equal(TimeSpan.FromMinutes(7), Props(cache).DurableTimeout);
    }

    [Fact]
    public async Task Mapping_Subscription_AutoReadyForEvents()
    {
        var cache = await CreateWith(o => o.Subscription.AutoReadyForEvents = false);
        Assert.False(Props(cache).AutoReadyForEvents);
    }

    [Fact]
    public async Task Mapping_Subscription_RedundancyMonitorInterval()
    {
        var cache = await CreateWith(o => o.Subscription.RedundancyMonitorInterval = TimeSpan.FromSeconds(33));
        Assert.Equal(TimeSpan.FromSeconds(33), Props(cache).RedundancyMonitorInterval);
    }

    [Fact]
    public async Task Mapping_Subscription_NotifyAckInterval()
    {
        var cache = await CreateWith(o => o.Subscription.NotifyAckInterval = TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(2), Props(cache).NotifyAckInterval);
    }

    [Fact]
    public async Task Mapping_Subscription_NotifyDupCheckLife()
    {
        var cache = await CreateWith(o => o.Subscription.NotifyDupCheckLife = TimeSpan.FromSeconds(123));
        Assert.Equal(TimeSpan.FromSeconds(123), Props(cache).NotifyDupCheckLife);
    }

    // ── Security ──────────────────────────────────────────────────

    // Mapping_Security_ClientDhAlgo: removed — SystemProperties.SecurityClientDhAlgo
    // is [Obsolete] (DH credentials encryption is not supported) and the
    // GeodeCache mapping was dropped accordingly. See SystemProperties.cs:86-88.

    [Fact]
    public async Task Mapping_Security_ClientKsPath()
    {
        var cache = await CreateWith(o => o.Security.ClientKsPath = "/path/to/ks.jks");
        Assert.Equal("/path/to/ks.jks", Props(cache).SecurityClientKsPath);
    }

    [Fact]
    public async Task Mapping_Security_Properties()
    {
        var cache = await CreateWith(o => o.Security.Properties["security-username"] = "admin");
        Assert.Equal("admin", Props(cache).SecurityProperties["security-username"]);
    }

    // ── Heap ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_Heap_LRULimit()
    {
        // ulong public ↔ long internal (cast at bridge — Phase 1.x wire is i64).
        var cache = await CreateWith(o => o.Heap.LRULimit = 1_000_000UL);
        Assert.Equal(1_000_000L, Props(cache).HeapLRULimit);
    }

    [Fact]
    public async Task Mapping_Heap_LRUDelta()
    {
        var cache = await CreateWith(o => o.Heap.LRUDelta = 25);
        Assert.Equal(25, Props(cache).HeapLRUDelta);
    }

    // ── Tls ───────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_Tls_Enabled()
    {
        var cache = await CreateWith(o => o.Tls.Enabled = true);
        Assert.True(Props(cache).SslEnabled);
    }

    // ── Pool ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_Pool_ConnectionPoolSize()
    {
        var cache = await CreateWith(o => o.Pool.ConnectionPoolSize = 12);
        Assert.Equal(12u, Props(cache).ConnectionPoolSize);
    }

    [Fact]
    public async Task Mapping_Pool_ConnectTimeout()
    {
        var cache = await CreateWith(o => o.Pool.ConnectTimeout = TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(15), Props(cache).ConnectTimeout);
    }

    [Fact]
    public async Task Mapping_Pool_ConnectWaitTimeout()
    {
        var cache = await CreateWith(o => o.Pool.ConnectWaitTimeout = TimeSpan.FromSeconds(8));
        Assert.Equal(TimeSpan.FromSeconds(8), Props(cache).ConnectWaitTimeout);
    }

    [Fact]
    public async Task Mapping_Pool_MaxSocketBufferSize()
    {
        var cache = await CreateWith(o => o.Pool.MaxSocketBufferSize = 128 * 1024);
        Assert.Equal(128 * 1024, Props(cache).MaxSocketBufferSize);
    }

    [Fact]
    public async Task Mapping_Pool_PingInterval()
    {
        var cache = await CreateWith(o => o.Pool.PingInterval = TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), Props(cache).PingInterval);
    }

    [Fact]
    public async Task Mapping_Pool_BucketWaitTimeout()
    {
        var cache = await CreateWith(o => o.Pool.BucketWaitTimeout = TimeSpan.FromMilliseconds(250));
        Assert.Equal(TimeSpan.FromMilliseconds(250), Props(cache).BucketWaitTimeout);
    }

    [Fact]
    public async Task Mapping_Pool_ShuffleEndpoints_InvertsToDisableShufflingEndpoint()
    {
        // ShuffleEndpoints=false → DisableShufflingEndpoint=true (cppcache parity).
        var cache = await CreateWith(o => o.Pool.ShuffleEndpoints = false);
        Assert.True(Props(cache).DisableShufflingEndpoint);
    }

    [Fact]
    public async Task Mapping_Pool_ShuffleEndpoints_DefaultTrue_LeavesDisableFalse()
    {
        var cache = await CreateWith(_ => { /* leave default true */ });
        Assert.False(Props(cache).DisableShufflingEndpoint);
    }

    // ── Serialization (set-able, post-init assignment) ────────────

    [Fact]
    public async Task Mapping_Serialization_MaxDepth()
    {
        var cache = await CreateWith(o => o.Serialization.MaxDepth = 16);
        Assert.Equal(16, Props(cache).MaxDepth);
    }

    [Fact]
    public async Task Mapping_Serialization_MaxArrayLength()
    {
        var cache = await CreateWith(o => o.Serialization.MaxArrayLength = 5_000);
        Assert.Equal(5_000, Props(cache).MaxArrayLength);
    }

    [Fact]
    public async Task Mapping_Serialization_MaxBytesLength()
    {
        var cache = await CreateWith(o => o.Serialization.MaxBytesLength = 50_000);
        Assert.Equal(50_000, Props(cache).MaxBytesLength);
    }

    [Fact]
    public async Task Mapping_Serialization_MaxStringLength()
    {
        var cache = await CreateWith(o => o.Serialization.MaxStringLength = 8_000);
        Assert.Equal(8_000, Props(cache).MaxStringLength);
    }

    // ── Build-time snapshot semantics ─────────────────────────────

    [Fact]
    public async Task PostBuildMutation_OnCapturedOptions_DoesNotAffectCache()
    {
        // Caller holds a ref to the GeodeClientOptions they configured;
        // mutating it after CreateAsync returns must not change the
        // cache's behaviour (cppcache "geode.properties → SystemProperties
        // at cache build" snapshot model).
        GeodeClientOptions? captured = null;
        await using var sp = BuildSp();
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c",
                (o, _) =>
                {
                    o.Serialization.MaxArrayLength = 100;
                    captured = o;
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(100, Props(cache).MaxArrayLength);

        // Late mutation on the original options bag.
        captured!.Serialization.MaxArrayLength = 999_999;

        // Cache stays on the build-time snapshot.
        Assert.Equal(100, Props(cache).MaxArrayLength);
    }

    // ── Helpers ───────────────────────────────────────────────────

    /// <summary>Shorthand: build factory + cache with <paramref name="configure"/>.</summary>
    private static async Task<IGeodeCache> CreateWith(Action<GeodeClientOptions> configure)
    {
        var sp = BuildSp();
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", (o, _) => configure(o), TestContext.Current.CancellationToken);
        // sp stays alive for the lifetime of the test method via cache holding refs.
        return cache;
    }
}
