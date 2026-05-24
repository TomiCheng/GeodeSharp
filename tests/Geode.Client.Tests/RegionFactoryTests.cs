using Geode.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests;

public class RegionFactoryTests
{
    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private static async Task<RegionFactory> BuildFactoryAsync(
        ServiceProvider sp, CancellationToken ct, RegionShortcut shortcut = RegionShortcut.Proxy)
    {
        var cache = await sp.GetRequiredService<IGeodeCacheFactory>().CreateAsync("c", ct);
        return cache.CreateRegionFactory(shortcut);
    }

    [Fact]
    public async Task Setters_ReturnSameFactoryInstance()
    {
        await using var sp = BuildSp();
        var f = await BuildFactoryAsync(sp, TestContext.Current.CancellationToken);

        Assert.Same(f, f.SetPoolName("p"));
        Assert.Same(f, f.SetInitialCapacity(64));
        Assert.Same(f, f.SetLoadFactor(0.5f));
        Assert.Same(f, f.SetConcurrencyLevel(8));
        Assert.Same(f, f.SetLruEntriesLimit(100));
        Assert.Same(f, f.SetCachingEnabled(false));
        Assert.Same(f, f.SetCloningEnabled(true));
        Assert.Same(f, f.SetConcurrencyChecksEnabled(false));
    }

    // CreateAsync orchestration tests live in RegionFactoryCreateAsyncTests.cs;
    // this file keeps the cheap surface-level facts (setter fluency).
}
