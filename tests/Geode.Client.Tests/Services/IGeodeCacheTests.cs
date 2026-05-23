using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

public class IGeodeCacheTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Name_MatchesCreateArgument()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var cache = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        Assert.Equal("foo", cache.Name);
    }

    [Fact]
    public async Task PoolManager_IsNonNull()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var cache = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        Assert.NotNull(cache.PoolManager);
    }

    [Fact]
    public async Task PoolManager_RepeatedReads_ReturnSameInstance()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var cache = await factory.CreateAsync("foo", TestContext.Current.CancellationToken);

        var first = cache.PoolManager;
        var second = cache.PoolManager;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task PoolManager_IsIsolatedPerCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var ct = TestContext.Current.CancellationToken;
        var cacheA = await factory.CreateAsync("a", ct);
        var cacheB = await factory.CreateAsync("b", ct);

        Assert.NotSame(cacheA.PoolManager, cacheB.PoolManager);
    }

}
