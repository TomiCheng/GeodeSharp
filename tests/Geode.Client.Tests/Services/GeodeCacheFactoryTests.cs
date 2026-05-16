using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Geode.Client.Tests.Services;

/// <summary>
/// Behaviour tests for <see cref="IGeodeCacheFactory"/> — the five-member
/// surface introduced by the DI redesign (<see cref="IGeodeCacheFactory.Get"/>,
/// <see cref="IGeodeCacheFactory.TryGet"/>,
/// <see cref="IGeodeCacheFactory.Create"/>,
/// <see cref="IGeodeCacheFactory.CacheNames"/>,
/// <see cref="IGeodeCacheFactory.RemoveAsync"/>).
/// </summary>
public class GeodeCacheFactoryTests
{
    private static void MinimalPool(GeodeClientOptions opt) =>
        opt.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "test",
                    Servers = { new CacheHostPortOptions { Host = "localhost", Port = 40404 } },
                },
            },
        };

    private static ServiceProvider BuildSp(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeClient(MinimalPool);
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // ── Get / TryGet without Create ──────────────────────────────

    [Fact]
    public async Task Get_WithoutCreate_Throws_KeyNotFound()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.Throws<KeyNotFoundException>(() => f.Get());
        Assert.Throws<KeyNotFoundException>(() => f.Get("any"));
    }

    [Fact]
    public async Task TryGet_WithoutCreate_ReturnsFalse()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.False(f.TryGet("", out var c1));
        Assert.Null(c1);

        Assert.False(f.TryGet("missing", out var c2));
        Assert.Null(c2);
    }

    // ── Create — happy path + double Create ─────────────────────

    [Fact]
    public async Task Create_ReturnsCacheRetrievableByGet_AndTryGet()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        var built = f.Create();
        Assert.Same(built, f.Get());
        Assert.True(f.TryGet("", out var via));
        Assert.Same(built, via);
    }

    [Fact]
    public async Task Create_SameCacheName_Twice_Throws_InvalidOperation()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        f.Create();
        var ex = Assert.Throws<InvalidOperationException>(() => f.Create());
        Assert.Contains("already exists", ex.Message);
    }

    // ── Create + action: clone semantics ────────────────────────

    [Fact]
    public async Task Create_Action_DoesNotMutate_RegisteredOptions()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();
        var monitor = sp.GetRequiredService<IOptionsMonitor<GeodeClientOptions>>();

        // Snapshot the registered options BEFORE Create's action runs.
        var beforePoolName = monitor.Get("").Cache!.Pools[0].Name;
        Assert.Equal("test", beforePoolName);

        f.Create(action: (_, o) =>
        {
            o.Cache!.Pools[0].Name = "mutated-by-action";
        });

        // Registered options must be untouched — the action ran on a clone.
        var afterPoolName = monitor.Get("").Cache!.Pools[0].Name;
        Assert.Equal("test", afterPoolName);
    }

    [Fact]
    public async Task Create_Action_SeesServiceProvider_Argument()
    {
        // The factory passes IServiceProvider into the action; verify it
        // is non-null and can resolve services. Root sp is what the
        // factory holds — should at minimum resolve ILoggerFactory.
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        IServiceProvider? seen = null;
        f.Create(action: (provider, _) => seen = provider);

        Assert.NotNull(seen);
        Assert.NotNull(seen!.GetService<ILoggerFactory>());
    }

    [Fact]
    public async Task Create_Action_ValidationFailure_Throws_OptionsValidationException()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        // Action breaks validation: clear all pools.
        var ex = Assert.Throws<OptionsValidationException>(() =>
            f.Create(action: (_, o) => o.Cache!.Pools.Clear()));

        Assert.Contains(ex.Failures, msg => msg.Contains("Pools"));

        // Factory not polluted by the failed Create.
        Assert.Empty(f.CacheNames);
    }

    // ── cacheName / configName decoupling ───────────────────────

    [Fact]
    public async Task Create_DifferentCacheNames_SameConfigName_BothWork()
    {
        // 1:N — one config feeds two cache slots (e.g. reader / writer
        // pools against the same cluster).
        await using var sp = BuildSp(s => s.AddGeodeFactory(MinimalPool, "shared-config"));
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        var writer = f.Create("writer", "shared-config");
        var reader = f.Create("reader", "shared-config");

        Assert.NotSame(writer, reader);
        Assert.Equal("writer", writer.Name);
        Assert.Equal("reader", reader.Name);
    }

    // ── CacheNames snapshot ─────────────────────────────────────

    [Fact]
    public async Task CacheNames_Empty_BeforeAnyCreate()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.Empty(f.CacheNames);
    }

    [Fact]
    public async Task CacheNames_ListsAllCreatedCaches()
    {
        await using var sp = BuildSp(s =>
        {
            s.AddGeodeFactory(MinimalPool, "g1");
            s.AddGeodeFactory(MinimalPool, "g2");
        });
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        f.Create();
        f.Create("g1", "g1");
        f.Create("g2", "g2");

        Assert.Equal(new[] { "", "g1", "g2" }, f.CacheNames.OrderBy(s => s));
    }

    [Fact]
    public async Task CacheNames_IsSnapshot_NotLiveView()
    {
        // Snapshot semantics: take a reference, then mutate factory
        // state; the reference must not change.
        await using var sp = BuildSp(s => s.AddGeodeFactory(MinimalPool, "g1"));
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        f.Create();
        var snapshot = f.CacheNames;

        f.Create("g1", "g1");

        Assert.Single(snapshot);
        Assert.Equal(2, f.CacheNames.Count);
    }

    // ── RemoveAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_Existing_DisposesCache_AndReturnsTrue()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        var cache = f.Create();
        Assert.False(cache.IsClosed);

        var removed = await f.RemoveAsync("");

        Assert.True(removed);
        Assert.True(cache.IsClosed);
        Assert.Empty(f.CacheNames);
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_ReturnsFalse()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.False(await f.RemoveAsync("never-created"));
    }

    [Fact]
    public async Task RemoveAsync_ThenCreate_SameName_BuildsFreshInstance()
    {
        await using var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        var first = f.Create();
        await f.RemoveAsync("");
        var second = f.Create();

        Assert.NotSame(first, second);
        Assert.True(first.IsClosed);
        Assert.False(second.IsClosed);
    }

    // ── disposed-factory contract ───────────────────────────────

    [Fact]
    public async Task AllOperations_AfterDispose_Throw_ObjectDisposed()
    {
        var sp = BuildSp();
        var f = sp.GetRequiredService<IGeodeCacheFactory>();

        await ((IAsyncDisposable)f).DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => f.Get());
        Assert.Throws<ObjectDisposedException>(() => f.TryGet("", out _));
        Assert.Throws<ObjectDisposedException>(() => f.Create());
        Assert.Throws<ObjectDisposedException>(() => _ = f.CacheNames);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await f.RemoveAsync(""));

        await sp.DisposeAsync();
    }
}
