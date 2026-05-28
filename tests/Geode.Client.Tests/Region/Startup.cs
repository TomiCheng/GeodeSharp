using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Tests.Region;

/// <summary>
/// Xunit.DependencyInjection Startup for the <c>Region</c> namespace.
/// Mirrors <see cref="ApiFirst.Startup"/> — registers the bare cache
/// factory; <see cref="IGeodeCacheFactory"/> resolves from there.
/// </summary>
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGeodeFactory();
    }
}
