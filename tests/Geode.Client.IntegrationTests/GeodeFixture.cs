using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// xUnit collection fixture that spins up an Apache Geode cluster in a
/// single container: <b>2 locators + 3 servers</b> with a pre-created
/// REPLICATE region named "test".
///
/// <para>
/// The richer topology (vs. 1 locator + 1 server) lets pool / locator /
/// failover logic actually exercise the code paths it would in
/// production: locator-list refresh sees multiple peers, the pool's
/// server-endpoint list has more than one candidate, and gfsh
/// <c>list members</c> verifies the client really joined a cluster
/// rather than a single-process degenerate case.
/// </para>
///
/// <para>
/// Host-side ports are pinned 1:1 to the container ports
/// (10334-10335 for locators, 40404-40406 for servers). Combined with
/// <c>--hostname-for-clients=localhost</c> on each server, this makes
/// the server address the locator hands back to clients
/// (<c>localhost:40404</c>, etc.) reachable from the test host — locator
/// mode end-to-end works. The downside is that at most one fixture
/// instance can run on a given host at a time; the xUnit collection
/// fixture already enforces single-instance per test run.
/// </para>
///
/// Usage:
///   [Collection(nameof(GeodeCollection))]
///   public class MyTests(GeodeFixture fx) { ... fx.LocatorEndpoint ... }
/// </summary>
public sealed class GeodeFixture : IAsyncLifetime
{
    private const int Locator1ContainerPort = 10334;
    private const int Locator2ContainerPort = 10335;
    private const int Server1ContainerPort = 40404;
    private const int Server2ContainerPort = 40405;
    private const int Server3ContainerPort = 40406;

    private IContainer? _container;

    public string LocatorHost { get; private set; } = "localhost";

    /// <summary>Primary (loc1) locator port — kept for backwards compat.</summary>
    public int LocatorPort { get; private set; } = Locator1ContainerPort;

    /// <summary>Secondary (loc2) locator port.</summary>
    public int LocatorPort2 { get; private set; } = Locator2ContainerPort;

    /// <summary>All locator ports, in start order (loc1, loc2).</summary>
    public IReadOnlyList<int> LocatorPorts { get; private set; } =
        new[] { Locator1ContainerPort, Locator2ContainerPort };

    /// <summary>Primary (srv1) server port — kept for backwards compat.</summary>
    public int ServerPort { get; private set; } = Server1ContainerPort;

    /// <summary>Secondary (srv2) server port.</summary>
    public int ServerPort2 { get; private set; } = Server2ContainerPort;

    /// <summary>Tertiary (srv3) server port.</summary>
    public int ServerPort3 { get; private set; } = Server3ContainerPort;

    /// <summary>All server ports, in start order (srv1, srv2, srv3).</summary>
    public IReadOnlyList<int> ServerPorts { get; private set; } =
        new[] { Server1ContainerPort, Server2ContainerPort, Server3ContainerPort };

    public string LocatorEndpoint => $"{LocatorHost}:{LocatorPort}";

    /// <summary>All locator endpoints ("host:port"), in start order.</summary>
    public IReadOnlyList<string> LocatorEndpoints =>
        LocatorPorts.Select(p => $"{LocatorHost}:{p}").ToArray();

    public async ValueTask InitializeAsync()
    {
        // The apachegeode/geode image's default entry runs `gfsh`, which
        // exits as soon as the supplied -e scripts finish — taking the
        // forked locators + servers down with it. Wrap in
        // `sh -c "...gfsh -e... && tail -F srv1.log"` so the container
        // stays alive (and tails the first server's log to stdout for
        // diagnostics).
        //
        // Both locators share a single `--locators=loc1,loc2` list so
        // they peer-discover each other; servers join the same list.
        // `--hostname-for-clients=localhost` on every member makes each
        // one register a host-reachable address: servers so the
        // ClientConnectionRequest reply gives the client a reachable
        // server, locators so the periodic LocatorListRequest refresh
        // doesn't overwrite the configured locator list with the
        // container-internal IP and break subsequent locator calls.
        // Combined with the fixed port mappings below.
        const string locators = "localhost[10334],localhost[10335]";
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
            // Fixed host-port = container-port so `--hostname-for-clients=localhost`
            // resolves to a port the client process on the host can reach.
            // (Random host ports would defeat that — locator returns 40404,
            // client tries localhost:40404, nothing there.)
            .WithPortBinding(Locator1ContainerPort, Locator1ContainerPort)
            .WithPortBinding(Locator2ContainerPort, Locator2ContainerPort)
            .WithPortBinding(Server1ContainerPort, Server1ContainerPort)
            .WithPortBinding(Server2ContainerPort, Server2ContainerPort)
            .WithPortBinding(Server3ContainerPort, Server3ContainerPort)
            .WithCommand(
                "sh", "-c",
                "mkdir -p /work && cd /work && "
                    + "gfsh "
                    + $"-e 'start locator --name=loc1 --port={Locator1ContainerPort} --hostname-for-clients=localhost --locators={locators}' "
                    + $"-e 'start locator --name=loc2 --port={Locator2ContainerPort} --hostname-for-clients=localhost --locators={locators}' "
                    + $"-e 'start server --name=srv1 --server-port={Server1ContainerPort} --hostname-for-clients=localhost --locators={locators}' "
                    + $"-e 'start server --name=srv2 --server-port={Server2ContainerPort} --hostname-for-clients=localhost --locators={locators}' "
                    + $"-e 'start server --name=srv3 --server-port={Server3ContainerPort} --hostname-for-clients=localhost --locators={locators}' "
                    + "-e 'create region --name=test --type=REPLICATE' "
                    + "&& tail -F /work/srv1/srv1.log")
            // Wait until every server's client port is listening — proves
            // all 3 JVMs reached the "ready for clients" state. Locator
            // ports come up earlier in the chain so they're implicitly
            // covered by the time the server ports are open.
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(Server1ContainerPort)
                    .UntilInternalTcpPortIsAvailable(Server2ContainerPort)
                    .UntilInternalTcpPortIsAvailable(Server3ContainerPort))
            .Build();

        await _container.StartAsync();

        LocatorHost = _container.Hostname;
        LocatorPort = _container.GetMappedPublicPort(Locator1ContainerPort);
        LocatorPort2 = _container.GetMappedPublicPort(Locator2ContainerPort);
        LocatorPorts = new[] { LocatorPort, LocatorPort2 };
        ServerPort = _container.GetMappedPublicPort(Server1ContainerPort);
        ServerPort2 = _container.GetMappedPublicPort(Server2ContainerPort);
        ServerPort3 = _container.GetMappedPublicPort(Server3ContainerPort);
        ServerPorts = new[] { ServerPort, ServerPort2, ServerPort3 };
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
        // first one connects to loc1 the container's own entry-point
        // started, the second is the caller's command. Single locator
        // is sufficient — gfsh learns the rest of the cluster from it.
        var result = await _container.ExecAsync(
            new[]
            {
                "gfsh",
                "-e", $"connect --locator=localhost[{Locator1ContainerPort}]",
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
