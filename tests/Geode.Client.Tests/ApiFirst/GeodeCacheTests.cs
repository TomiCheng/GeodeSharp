using Xunit;

namespace Geode.Client.Tests.ApiFirst;

/// <summary>
/// Public-surface tests for <see cref="IGeodeCacheFactory"/> — covers the
/// add / lookup / delete contract through the interface, no internals.
/// Each test uses a unique cache name (test method name) so they're
/// independent across the shared <see cref="IGeodeCacheFactory"/> instance.
/// </summary>
public class GeodeCacheTests(IGeodeCacheFactory factory)
{
    // ── Create ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsNonNullCache()
    {
        const string name = nameof(CreateAsync_ReturnsNonNullCache);
        try
        {
            var cache = await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(cache);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task CreateAsync_RegistersUnderName()
    {
        const string name = nameof(CreateAsync_RegistersUnderName);
        try
        {
            await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            Assert.Contains(name, factory.CacheNames);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_Throws()
    {
        const string name = nameof(CreateAsync_DuplicateName_Throws);
        try
        {
            await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => factory.CreateAsync(name, ct: TestContext.Current.CancellationToken));
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    // ── Get / TryGet ───────────────────────────────────────────────

    [Fact]
    public async Task Get_AfterCreate_ReturnsSameInstance()
    {
        const string name = nameof(Get_AfterCreate_ReturnsSameInstance);
        try
        {
            var created = await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            var fetched = factory.Get(name);
            Assert.Same(created, fetched);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public void Get_UnknownName_ThrowsKeyNotFound()
    {
        Assert.Throws<KeyNotFoundException>(
            () => factory.Get("does-not-exist-" + Guid.NewGuid()));
    }

    [Fact]
    public async Task TryGet_AfterCreate_ReturnsTrueAndSameInstance()
    {
        const string name = nameof(TryGet_AfterCreate_ReturnsTrueAndSameInstance);
        try
        {
            var created = await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            Assert.True(factory.TryGet(name, out var fetched));
            Assert.Same(created, fetched);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        Assert.False(factory.TryGet(
            "does-not-exist-" + Guid.NewGuid(), out var cache));
        Assert.Null(cache);
    }

    // ── Delete ─────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeCacheAsync_AfterCreate_ReturnsTrueAndRemoves()
    {
        const string name = nameof(DisposeCacheAsync_AfterCreate_ReturnsTrueAndRemoves);
        await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
        Assert.True(await factory.DisposeCacheAsync(name));
        Assert.False(factory.TryGet(name, out _));
        Assert.DoesNotContain(name, factory.CacheNames);
    }

    [Fact]
    public async Task DisposeCacheAsync_UnknownName_ReturnsFalse()
    {
        Assert.False(await factory.DisposeCacheAsync(
            "does-not-exist-" + Guid.NewGuid()));
    }

    [Fact]
    public async Task CreateAsync_AfterDispose_AllowsReuseOfSameName()
    {
        const string name = nameof(CreateAsync_AfterDispose_AllowsReuseOfSameName);
        try
        {
            await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            await factory.DisposeCacheAsync(name);
            var second = await factory.CreateAsync(name, ct: TestContext.Current.CancellationToken);
            Assert.NotNull(second);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }
}
