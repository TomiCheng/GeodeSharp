using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

public class IGeodeCacheFactoryTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    // ── CreateAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NewName_ReturnsNonNullCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var cache = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        Assert.NotNull(cache);
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsInvalidOperationException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync("foo", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.CreateAsync("foo", TestContext.Current.CancellationToken));
    }

    // ── Get / TryGet ──────────────────────────────────────────────

    [Fact]
    public async Task Get_AfterCreate_ReturnsSameInstance()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var created = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        var retrieved = factory.Get("foo");

        Assert.Same(created, retrieved);
    }

    [Fact]
    public async Task Get_UnknownName_ThrowsKeyNotFoundException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.Throws<KeyNotFoundException>(() => factory.Get("nope"));
    }

    [Fact]
    public async Task TryGet_AfterCreate_ReturnsTrueWithCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var created = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        var found = factory.TryGet("foo", out var cache);

        Assert.True(found);
        Assert.Same(created, cache);
    }

    [Fact]
    public async Task TryGet_UnknownName_ReturnsFalseAndNullCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var found = factory.TryGet("nope", out var cache);

        Assert.False(found);
        Assert.Null(cache);
    }

    // ── CacheNames ────────────────────────────────────────────────

    [Fact]
    public async Task CacheNames_Initially_Empty()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.Empty(factory.CacheNames);
    }

    [Fact]
    public async Task CacheNames_ListsCreatedCaches()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.CreateAsync("foo", TestContext.Current.CancellationToken);
        await factory.CreateAsync("bar", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "bar", "foo" }, factory.CacheNames.OrderBy(n => n));
    }

    [Fact]
    public async Task CacheNames_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => factory.CacheNames);
    }

    // ── DisposeCacheAsync ─────────────────────────────────────────

    [Fact]
    public async Task DisposeCacheAsync_ExistingName_ReturnsTrueAndRemoves()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        var removed = await factory.DisposeCacheAsync("foo");

        Assert.True(removed);
        Assert.False(factory.TryGet("foo", out _));
    }

    [Fact]
    public async Task DisposeCacheAsync_UnknownName_ReturnsFalse()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var removed = await factory.DisposeCacheAsync("nope");

        Assert.False(removed);
    }

    [Fact]
    public async Task DisposeCacheAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await factory.DisposeCacheAsync("foo"));
    }

    // ── DisposeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_Idempotent()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        await factory.DisposeAsync();
        await factory.DisposeAsync();
    }
}
