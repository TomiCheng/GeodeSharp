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

    // ── Create ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_NewName_ReturnsNonNullCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var cache = factory.Create("foo");

        Assert.NotNull(cache);
    }

    [Fact]
    public async Task Create_DuplicateName_ThrowsInvalidOperationException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        factory.Create("foo");

        Assert.Throws<InvalidOperationException>(() => factory.Create("foo"));
    }

    [Fact]
    public async Task Create_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        await factory.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => factory.Create("foo"));
    }

    // ── Get / TryGet ──────────────────────────────────────────────

    [Fact]
    public async Task Get_AfterCreate_ReturnsSameInstance()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var created = factory.Create("foo");

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
        var created = factory.Create("foo");

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
        factory.Create("foo");
        factory.Create("bar");

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
        factory.Create("foo");

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
