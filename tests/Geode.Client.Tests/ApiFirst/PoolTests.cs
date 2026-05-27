using Xunit;

namespace Geode.Client.Tests.ApiFirst;

/// <summary>
/// Public-surface tests for <see cref="IPoolManager"/> + <see cref="IPoolFactory"/>
/// — covers the registry / lookup contract and the factory builder's
/// fluent / validation behaviour. No real network: tests stop before
/// <see cref="IPoolFactory.BuildAsync"/> opens sockets.
/// </summary>
public class PoolTests(IGeodeCacheFactory factory)
{
    /// <summary>One cache per test method — name uniqueness avoids cross-test pollution on the shared factory.</summary>
    private Task<IGeodeCache> NewCacheAsync(string name) =>
        factory.CreateAsync(name, ct: default);

    // ── PoolManager: empty state ───────────────────────────────────

    [Fact]
    public async Task PoolManager_BeforeAnyBuild_DefaultPoolIsNull()
    {
        const string name = nameof(PoolManager_BeforeAnyBuild_DefaultPoolIsNull);
        try
        {
            var cache = await NewCacheAsync(name);
            Assert.Null(cache.PoolManager.DefaultPool);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolManager_BeforeAnyBuild_GetAllIsEmpty()
    {
        const string name = nameof(PoolManager_BeforeAnyBuild_GetAllIsEmpty);
        try
        {
            var cache = await NewCacheAsync(name);
            Assert.Empty(cache.PoolManager.GetAll());
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolManager_Find_UnknownName_ReturnsNull()
    {
        const string name = nameof(PoolManager_Find_UnknownName_ReturnsNull);
        try
        {
            var cache = await NewCacheAsync(name);
            Assert.Null(cache.PoolManager.Find("nope-" + Guid.NewGuid()));
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolManager_Find_NullName_ReturnsDefaultPool()
    {
        const string name = nameof(PoolManager_Find_NullName_ReturnsDefaultPool);
        try
        {
            var cache = await NewCacheAsync(name);
            // No pool registered yet → default is null → Find(null) is null.
            Assert.Same(cache.PoolManager.DefaultPool, cache.PoolManager.Find((string?)null));
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    // ── PoolFactory: shape + fluent chaining ───────────────────────

    [Fact]
    public async Task CreateFactory_ReturnsNonNullIPoolFactory()
    {
        const string name = nameof(CreateFactory_ReturnsNonNullIPoolFactory);
        try
        {
            var cache = await NewCacheAsync(name);
            var pf = cache.PoolManager.CreateFactory();
            Assert.NotNull(pf);
            Assert.IsAssignableFrom<IPoolFactory>(pf);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task CreateFactory_EachCall_ReturnsDistinctInstance()
    {
        const string name = nameof(CreateFactory_EachCall_ReturnsDistinctInstance);
        try
        {
            var cache = await NewCacheAsync(name);
            var a = cache.PoolManager.CreateFactory();
            var b = cache.PoolManager.CreateFactory();
            Assert.NotSame(a, b);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolFactory_Setters_AreFluent()
    {
        const string name = nameof(PoolFactory_Setters_AreFluent);
        try
        {
            var cache = await NewCacheAsync(name);
            var pf = cache.PoolManager.CreateFactory();

            // A representative sample — every setter follows the same
            // "mutate _attrs, return this" pattern, so chaining a few
            // proves the contract.
            var chained = pf
                .SetMinConnections(1)
                .SetMaxConnections(5)
                .SetReadTimeout(TimeSpan.FromSeconds(10))
                .SetSubscriptionEnabled(false)
                .AddServer("localhost", 40404);

            Assert.Same(pf, chained);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolFactory_AddLocator_AfterAddServer_Throws()
    {
        const string name = nameof(PoolFactory_AddLocator_AfterAddServer_Throws);
        try
        {
            var cache = await NewCacheAsync(name);
            var pf = cache.PoolManager.CreateFactory();
            pf.AddServer("localhost", 40404);
            Assert.Throws<ArgumentException>(
                () => pf.AddLocator("localhost", 10334));
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolFactory_AddServer_AfterAddLocator_Throws()
    {
        const string name = nameof(PoolFactory_AddServer_AfterAddLocator_Throws);
        try
        {
            var cache = await NewCacheAsync(name);
            var pf = cache.PoolManager.CreateFactory();
            pf.AddLocator("localhost", 10334);
            Assert.Throws<ArgumentException>(
                () => pf.AddServer("localhost", 40404));
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }

    [Fact]
    public async Task PoolFactory_Reset_ClearsEndpoints()
    {
        const string name = nameof(PoolFactory_Reset_ClearsEndpoints);
        try
        {
            var cache = await NewCacheAsync(name);
            var pf = cache.PoolManager.CreateFactory();
            pf.AddServer("localhost", 40404);
            pf.Reset();
            // After Reset, opposite endpoint kind should be re-addable —
            // the invariant the Add*-after-Add* exceptions enforce.
            pf.AddLocator("localhost", 10334);
        }
        finally
        {
            await factory.DisposeCacheAsync(name);
        }
    }
}
