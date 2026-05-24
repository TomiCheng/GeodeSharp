using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

public class GeodeCacheFactoryCreationTests
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
    public async Task AddGeodeFactory_Registers_IGeodeCacheFactory()
    {
        await using var sp = BuildSp();

        var factory = sp.GetService<IGeodeCacheFactory>();

        Assert.NotNull(factory);
    }

    [Fact]
    public async Task IGeodeCacheFactory_Resolves_AsSingleton()
    {
        await using var sp = BuildSp();

        var first = sp.GetRequiredService<IGeodeCacheFactory>();
        var second = sp.GetRequiredService<IGeodeCacheFactory>();

        Assert.Same(first, second);
    }
}
