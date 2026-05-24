using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

public class IPoolManagerTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static async Task<IPoolManager> BuildManagerAsync(ServiceProvider sp, CancellationToken ct)
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        return cache.PoolManager;
    }

    private static Task<IPool> BuildPoolAsync(IPoolManager mgr, string name, CancellationToken ct)
        => mgr.CreateFactory().AddServer("h", 40404).BuildAsync(name, ct);

    // ── DefaultPool ───────────────────────────────────────────────

    [Fact]
    public async Task DefaultPool_Initially_IsNull()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);

        Assert.Null(mgr.DefaultPool);
    }

    [Fact]
    public async Task DefaultPool_AfterBuild_ReturnsFirstPool()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        var p1 = await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);

        Assert.Same(p1, mgr.DefaultPool);
    }

    [Fact]
    public async Task DefaultPool_StaysFirst_AfterSecondBuild()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        var p1 = await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);
        var p2 = await BuildPoolAsync(mgr, "p2", TestContext.Current.CancellationToken);

        Assert.Same(p1, mgr.DefaultPool);
        Assert.NotSame(p2, mgr.DefaultPool);
    }

    // ── Find ──────────────────────────────────────────────────────

    [Fact]
    public async Task Find_NullName_NoPools_ReturnsNull()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);

        Assert.Null(mgr.Find());
    }

    [Fact]
    public async Task Find_NullName_AfterBuild_ReturnsDefaultPool()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        var p1 = await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);

        Assert.Same(p1, mgr.Find());
    }

    [Fact]
    public async Task Find_UnknownName_ReturnsNull()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);

        Assert.Null(mgr.Find("nope"));
    }

    [Fact]
    public async Task Find_KnownName_ReturnsThatPool()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        var p1 = await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);
        var p2 = await BuildPoolAsync(mgr, "p2", TestContext.Current.CancellationToken);

        Assert.Same(p1, mgr.Find("p1"));
        Assert.Same(p2, mgr.Find("p2"));
    }

    // ── GetAll ────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_Empty_Initially()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);

        Assert.Empty(mgr.GetAll());
    }

    [Fact]
    public async Task GetAll_AfterBuild_ContainsPools()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        var p1 = await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);
        var p2 = await BuildPoolAsync(mgr, "p2", TestContext.Current.CancellationToken);

        var all = mgr.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Same(p1, all["p1"]);
        Assert.Same(p2, all["p2"]);
    }

    [Fact]
    public async Task GetAll_ReturnsSnapshot_NotLiveView()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);

        var snapshot = mgr.GetAll();
        await BuildPoolAsync(mgr, "p2", TestContext.Current.CancellationToken);

        // Snapshot taken before "p2" was added: it stays at 1 entry.
        Assert.Single(snapshot);
        Assert.Equal(2, mgr.GetAll().Count);
    }

    // ── CloseAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_Empty_DoesNotThrow()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);

        await mgr.CloseAsync(ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CloseAsync_AfterBuild_ClearsRegistry()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);
        await BuildPoolAsync(mgr, "p1", TestContext.Current.CancellationToken);

        await mgr.CloseAsync(ct: TestContext.Current.CancellationToken);

        Assert.Null(mgr.Find("p1"));
        Assert.Null(mgr.DefaultPool);
        Assert.Empty(mgr.GetAll());
    }

    [Fact]
    public async Task CloseAsync_Idempotent()
    {
        await using var sp = BuildSp();
        var mgr = await BuildManagerAsync(sp, TestContext.Current.CancellationToken);

        await mgr.CloseAsync(ct: TestContext.Current.CancellationToken);
        await mgr.CloseAsync(ct: TestContext.Current.CancellationToken);
    }
}
