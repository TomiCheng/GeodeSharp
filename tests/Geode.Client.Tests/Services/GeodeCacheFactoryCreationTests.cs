using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

public class GeodeCacheFactoryCreationTests(IGeodeCacheFactory factory)
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGeodeFactory();
        }
    }

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
        Assert.NotNull(factory);
    }
}
