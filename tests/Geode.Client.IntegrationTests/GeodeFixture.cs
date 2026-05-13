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
        // The apachegeode/geode image's default entry runs `gfsh`, which
        // exits as soon as the supplied -e scripts finish — taking the
        // forked locator + server down with it. Wrap in `sh -c "...gfsh -e... &&
        // tail -f $log"` so the container stays alive (and tails the server
        // log to stdout for diagnostics).
        _container = new ContainerBuilder()
            .WithImage("apachegeode/geode:latest")
            // Pin the container's timezone so Java Date.toString() (and
            // anything else reading the JVM's default zone) is
            // deterministic across dev boxes / CI. Our DateTime
            // converter encodes wire bytes as UTC ms-since-epoch, so
            // matching the container TZ to UTC keeps server-side
            // verification (gfsh `get` printing Date.toString()) stable
            // and the assertion text readable.
            .WithEnvironment("TZ", "UTC")
            .WithPortBinding(10334, true)
            .WithPortBinding(40404, true)
            .WithCommand(
                "sh", "-c",
                "gfsh "
                    + "-e 'start locator --name=loc --port=10334' "
                    + "-e 'start server --name=srv --server-port=40404' "
                    + "-e 'create region --name=test --type=REPLICATE' "
                    + "&& tail -f /srv/srv.log")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(40404))
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

    /// <summary>
    /// Runs a gfsh command inside the running container after connecting
    /// to the locator, and returns the combined stdout.
    ///
    /// <para>
    /// Used by integration tests to verify that what our client wrote
    /// was deserialized by the server into the expected Java type — a
    /// guarantee that a pure round-trip Put/Get assertion cannot make,
    /// because a symmetric encoder/decoder bug would still round-trip
    /// successfully while leaving the server with garbage bytes. See
    /// also: discussion in PROGRESS.md Phase 1.3.0.
    /// </para>
    /// <para>
    /// Output format is gfsh's tabular text (e.g. <c>Value Class :
    /// java.lang.Integer</c>); tests parse it with <c>Assert.Contains</c>
    /// against literal expected lines rather than building a structured
    /// parser — fewer moving parts, and any gfsh format change will fail
    /// loudly with a readable diff.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if gfsh exits with a non-zero exit code; the captured
    /// stdout + stderr are surfaced in the exception message so the
    /// test failure points at the actual gfsh error (region missing,
    /// type-class mismatch, etc.).
    /// </exception>
    public async Task<string> GfshAsync(string command, CancellationToken ct)
    {
        if (_container is null)
        {
            throw new InvalidOperationException(
                "GeodeFixture has not been initialized; call InitializeAsync first.");
        }

        // -e scripts run sequentially in the same gfsh process; the
        // first one connects to the locator the container's own
        // entry-point started, the second is the caller's command.
        var result = await _container.ExecAsync(
            new[]
            {
                "gfsh",
                "-e", "connect --locator=localhost[10334]",
                "-e", command,
            },
            ct);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"gfsh '{command}' exited with code {result.ExitCode}.\n"
                + $"stdout:\n{result.Stdout}\n"
                + $"stderr:\n{result.Stderr}");
        }

        return result.Stdout;
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
