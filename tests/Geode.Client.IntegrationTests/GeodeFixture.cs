using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// xUnit collection fixture that spins up an Apache Geode container with
/// a pre-created REPLICATE region named "test".
///
/// Usage:
///   [Collection(nameof(GeodeCollection))]
///   public class MyTests(GeodeFixture fx) { ... fx.LocatorEndpoint ... }
/// </summary>
public sealed class GeodeFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string LocatorHost { get; private set; } = "localhost";
    public int LocatorPort { get; private set; } = 10334;
    public int ServerPort { get; private set; } = 40404;
    public string LocatorEndpoint => $"{LocatorHost}:{LocatorPort}";

    public async ValueTask InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("apachegeode/geode:latest")
            .WithPortBinding(10334, true)
            .WithPortBinding(40404, true)
            .WithCommand(
                "gfsh", "-e", "start locator --name=loc --port=10334",
                "-e", "start server --name=srv --server-port=40404",
                "-e", "create region --name=test --type=REPLICATE")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10334))
            .Build();

        await _container.StartAsync();

        LocatorHost = _container.Hostname;
        LocatorPort = _container.GetMappedPublicPort(10334);
        ServerPort = _container.GetMappedPublicPort(40404);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(nameof(GeodeCollection))]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "xUnit [CollectionDefinition] uses the class name as the collection identifier; renaming away from the 'Collection' suffix would break the [Collection(nameof(GeodeCollection))] usage convention.")]
public sealed class GeodeCollection : ICollectionFixture<GeodeFixture>
{
}
