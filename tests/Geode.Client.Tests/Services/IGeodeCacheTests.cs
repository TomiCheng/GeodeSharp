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
    public async Task Name_MatchesFactoryCreateArgument()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();

        var cache = factory.Create("foo");

        Assert.Equal("foo", cache.Name);
    }

    [Fact]
    public async Task PoolManager_IsNonNull()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var cache = factory.Create("foo");

        Assert.NotNull(cache.PoolManager);
    }

    [Fact]
    public async Task PoolManager_RepeatedReads_ReturnSameInstance()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var cache = factory.Create("foo");

        var first = cache.PoolManager;
        var second = cache.PoolManager;

        Assert.Same(first, second);
    }

    [Fact]
    public async Task PoolManager_IsIsolatedPerCache()
    {
        await using var sp = BuildSp();
        var factory = sp.GetRequiredService<IGeodeCacheFactory>();
        var cacheA = factory.Create("a");
        var cacheB = factory.Create("b");

        Assert.NotSame(cacheA.PoolManager, cacheB.PoolManager);
    }
}
