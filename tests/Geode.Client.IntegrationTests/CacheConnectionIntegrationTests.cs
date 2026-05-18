using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.1 end-to-end test: open one TCP connection to a real
/// Apache Geode server through the public <see cref="IGeodeCache"/>
/// API, then close it cleanly. No pool, no multi-endpoint, no
/// failover &#x2014; just the consumer-visible
/// <c>EnsureInitializedAsync</c> / <c>CloseAsync</c> contract.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class CacheConnectionIntegrationTests(GeodeFixture fx)
{
    private readonly GeodeFixture _fx = fx;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Path-(a) declarative config: one named pool with one server
    /// pointing at the fixture container. Equivalent to a cache.xml
    /// <c>&lt;pool&gt;&lt;server host="..." port="..."/&gt;&lt;/pool&gt;</c>.
    /// </summary>
    private void ConfigureCache(GeodeClientOptions config)
    {
        config.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "testPool",
                    Servers =
                    {
                        new CacheHostPortOptions
                        {
                            Host = _fx.LocatorHost,
                            Port = _fx.ServerPort,
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public async Task EnsureInitializedAsync_opens_connection_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        Assert.False(cache.IsClosed);

        // Phase 1.1 goal: open a single TCP connection, run handshake,
        // become reachable for Ping / future ops. Should not throw.
        await cache.EnsureInitializedAsync(cts.Token);

        // Idempotent — second call must not re-handshake or fail.
        await cache.EnsureInitializedAsync(cts.Token);

        // Phase 1.1 goal: send CloseConnection(18), drain in-flight,
        // dispose endpoint cleanly.
        await cache.CloseAsync(cts.Token);

        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task CloseAsync_is_idempotent()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        await cache.CloseAsync(cts.Token);
        await cache.CloseAsync(cts.Token); // second call: no-op, must not throw

        Assert.True(cache.IsClosed);
    }

    [Fact]
    public async Task ConnManageLoop_opens_first_connection_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Tighten IdleTimeout so the conn-management loop's first tick
        // fires in ~100 ms instead of the 10 s default — keeps the test
        // fast and avoids CI flakiness against the default.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        IdleTimeout = TimeSpan.FromMilliseconds(100),
                    },
                },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // Walk Cache → PoolManager → DefaultPool → ThinClientPoolDM to
        // observe _poolSize. Cache.PoolManager is an internal test hook;
        // ThinClientPoolDM.PoolSize wraps Volatile.Read(ref _poolSize).
        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;

        // Phase 1.1 walking-skeleton goal: ConnManageLoop fires →
        // RestoreMinConnections → CreatePoolConnectionAsync → endpoint
        // opens TCP + handshake → pool size becomes 1. Poll because the
        // loop wakes async; 5 s is generous against a cold container.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && pool.PoolSize < 1)
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            pool.PoolSize >= 1,
            $"Expected pool.PoolSize >= 1 within deadline, got {pool.PoolSize}.");

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task PoolConnections_gauge_reports_current_pool_size()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Subscribe before cache init so the ObservableGauge instrument
        // is picked up regardless of static-field init ordering.
        using var poolConnections = new MeterCapture("Geode.Client.Pool", "PoolConnections");

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        IdleTimeout = TimeSpan.FromMilliseconds(100),
                    },
                },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;

        // Wait for the conn-management loop to bring pool size to >= 1
        // (same path as ConnManageLoop_opens_first_connection_against_real_server).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && pool.PoolSize < 1)
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            pool.PoolSize >= 1,
            $"Pool failed to open MinConnections within deadline; PoolSize={pool.PoolSize}.");

        // Pull the gauge — ObservableGauge fires its callback synchronously
        // and routes through the listener back into MeterCapture.LastValue.
        poolConnections.Observe();
        Assert.True(
            poolConnections.LastValue >= 1,
            $"Expected PoolConnections gauge >= 1 after pool opens MinConnections, got {poolConnections.LastValue}.");

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task CleanStaleConnections_idle_path_shrinks_pool_and_bumps_IdleDisconnects()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        using var idleDisconnects = new MeterCapture("Geode.Client.Pool", "IdleDisconnects");
        using var poolConnections = new MeterCapture("Geode.Client.Pool", "PoolConnections");

        // MinConnections = 0 so the floor doesn't block idle removal.
        // IdleTimeout = 200ms doubles as the ConnManageLoop sweep interval +
        // the isIdle threshold. LoadConditioningInterval = 10min effectively
        // disables the load-cond path so only the idle branch can fire.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        MinConnections = 0,
                        IdleTimeout = TimeSpan.FromMilliseconds(200),
                        LoadConditioningInterval = TimeSpan.FromMinutes(10),
                        // Disable ping loop so it doesn't open conns mid-test.
                        PingInterval = TimeSpan.Zero,
                    },
                },
                Regions = { new CacheRegionOptions { Name = "test" } },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // One op forces a lazy conn open + return to queue. After that conn
        // sits idle for IdleTimeout, the next CleanStale sweep removes it
        // (Min=0 means the floor doesn't save it).
        // Settle delay covers the fresh-conn race against a cold container
        // (memory geode-fresh-conn-race.md): Put on a fresh conn within
        // ~100ms of open can hit RegionDestroyedException before the server
        // finishes per-conn ClientHealthMonitor registration.
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

        var region = cache.GetRegion<int, int>("test");
        Assert.NotNull(region);
        await region.PutAsync(0x6000_0001, 1234, cts.Token);

        // Poll for BOTH the counter bump AND the pool draining — once
        // they're both true the system is in steady state and we can
        // assert without race.
        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && (idleDisconnects.Count < 1 || pool.PoolSize != 0))
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            idleDisconnects.Count >= 1,
            $"Expected IdleDisconnects >= 1 within deadline, got {idleDisconnects.Count}.");

        poolConnections.Observe();
        Assert.Equal(0, (int)poolConnections.LastValue);

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task CleanStaleConnections_loadCond_path_bumps_LoadConditioningDisconnects()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        using var lcDisconnects = new MeterCapture("Geode.Client.Pool", "LoadConditioningDisconnects");

        // MinConnections = 0 — load conditioning takes the pure-shrink path
        // (replaceCount <= 0) instead of replace. Replace path with a single
        // configured server would hit the currentServer recycle hint
        // (cppcache L1760-1765): SelectEndpoint picks the same endpoint, the
        // dying conn gets UpdateCreationTime'd and returned — no new conn,
        // no LoadConditioningConnect/Disconnect bumps. That parity is
        // correct; testing it would require a multi-server fixture.
        //
        // LoadConditioningInterval = 50ms — well under sweep cadence (IdleTimeout
        // = 200ms) so HasExpired fires before IsIdle's first eligible window
        // (IsIdle requires unused > effectiveIdle = min(IdleTimeout, LoadCond)).
        // PingInterval = 0 keeps the ping loop from opening conns mid-test.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        MinConnections = 0,
                        IdleTimeout = TimeSpan.FromMilliseconds(200),
                        LoadConditioningInterval = TimeSpan.FromMilliseconds(50),
                        PingInterval = TimeSpan.Zero,
                    },
                },
                Regions = { new CacheRegionOptions { Name = "test" } },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        // Fresh-conn settle (memory geode-fresh-conn-race.md) + force a
        // lazy conn open via Put. After the conn is returned to the queue
        // and ages past LoadCond (~50ms), the next sweep flags it as
        // LoadConditioning and pure-shrinks it.
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

        var region = cache.GetRegion<int, int>("test");
        Assert.NotNull(region);
        await region.PutAsync(0x6000_0002, 1234, cts.Token);

        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && (lcDisconnects.Count < 1 || pool.PoolSize != 0))
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            lcDisconnects.Count >= 1,
            $"Expected LoadConditioningDisconnects >= 1 within deadline, got {lcDisconnects.Count}.");

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task ConnManageLoop_opens_MinConnections_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // MinConnections = 2 forces RestoreMinConnectionsAsync to loop
        // CreatePoolConnectionAsync twice in the same tick — exercises
        // (a) AddEPAsync deduping the second call to the same endpoint,
        // (b) two distinct TcrConnection instances opened via DI,
        // (c) two enqueues into _opConnections.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        MinConnections = 2,
                        IdleTimeout = TimeSpan.FromMilliseconds(100),
                    },
                },
            })
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && pool.PoolSize < 2)
        {
            await Task.Delay(50, cts.Token);
        }
        Assert.True(
            pool.PoolSize >= 2,
            $"Expected pool.PoolSize >= 2 within deadline, got {pool.PoolSize}.");

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task PingLoop_pings_endpoint_against_real_server()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        // Tight intervals for a fast test:
        //   IdleTimeout=100ms → ConnManageLoop pre-fills the pool
        //     (RestoreMinConnectionsAsync) within ~100ms.
        //   PingInterval=200ms → 5 ticks per second, plenty within 5s.
        //   MinConnections=1 → guarantees one conn sits in _opConnections
        //     for SendRequestToEndpointAsync's GetFromEPAsync to borrow.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config => config.Cache = new CacheOptions
            {
                Pools =
                {
                    new CachePoolOptions
                    {
                        Name = "testPool",
                        Servers =
                        {
                            new CacheHostPortOptions
                            {
                                Host = _fx.LocatorHost,
                                Port = _fx.ServerPort,
                            },
                        },
                        MinConnections = 1,
                        IdleTimeout = TimeSpan.FromMilliseconds(100),
                        PingInterval = TimeSpan.FromMilliseconds(200),
                    },
                },
            })
            .BuildServiceProvider();

        using var pingSweeps = new MeterCapture("Geode.Client.Pool", "PingSweepTime");
        using var endpointPings = new MeterCapture("Geode.Client.Pool", "EndpointPingTime");

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        var pool = (ThinClientPoolDM)((Cache)cache).PoolManager.DefaultPool!;

        // Two independent assertions, both must hold:
        //   (1) PingSweepTime.Count >= 3 → ping loop is alive (PeriodicTimer
        //       firing, foreach completing, finally always records).
        //   (2) EndpointPingTime.Count >= 2 → at least one PingAsync returned
        //       without throwing AND endpoint stayed connected. Cppcache's
        //       _msgSent / _pingSent short-circuit lets a tick complete as
        //       success without sending bytes, so >= 2 (rather than == 3)
        //       tolerates that pattern. >= 2 still proves the first real
        //       ping succeeded — failure would have flipped IsConnected
        //       and the histogram wouldn't have recorded.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline
               && (pingSweeps.Count < 3 || endpointPings.Count < 2))
        {
            await Task.Delay(50, cts.Token);
        }

        Assert.True(
            pingSweeps.Count >= 3,
            $"Expected PingSweepTime.Count >= 3 within deadline, got {pingSweeps.Count}.");
        Assert.True(
            endpointPings.Count >= 2,
            $"Expected EndpointPingTime.Count >= 2 within deadline, got {endpointPings.Count}.");

        // Sanity: pool conn was returned to the queue after each ping —
        // PoolSize must not have drained even though ping borrowed conns.
        Assert.True(
            pool.PoolSize >= 1,
            $"Expected pool.PoolSize >= 1 after ping sweeps, got {pool.PoolSize}.");

        await cache.CloseAsync(cts.Token);
    }

    [Fact]
    public async Task DisposeAsync_closes_underlying_connection()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        IGeodeCache cache;
        await using (var services = new ServiceCollection()
                         .AddLogging()
                         .AddGeodeClient(ConfigureCache)
                         .BuildServiceProvider())
        {
            cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
            await cache.EnsureInitializedAsync(cts.Token);
        }
        // ServiceProvider disposal cascades into the
        // GeodeCacheFactory's per-cache scope, which disposes Cache,
        // which disposes the TcrEndpoint, which sends
        // CloseConnection(18) and closes the socket.

        Assert.True(cache.IsClosed);
    }
}
