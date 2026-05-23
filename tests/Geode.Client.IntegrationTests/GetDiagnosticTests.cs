/*
using Geode.Client.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Diagnostic dump for Get on a known-missing region. Helps understand
/// what the server actually sends back when the region isn't found, and
/// confirms our wire encoding for the region-name and key parts.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class GetDiagnosticTests(GeodeFixture fx, ITestOutputHelper output)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    [Fact(Skip = "Diagnostic dump used to investigate the connection-state " +
        "issue around Get on a fresh connection. Run manually by removing this " +
        "Skip when re-investigating.")]
    public async Task Dump_Get_request_bytes_and_reply_for_missing_region()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config)
            .BuildServiceProvider();

        var connection = services.GetRequiredService<TcrConnection>();
        var builder = services.GetRequiredService<TcrMessageBuilder>();

        await connection.ConnectAsync(fx.LocatorHost, fx.ServerPort, cancellationToken: cts.Token);
        output.WriteLine("Connected; handshake OK.");

        // ---- 1. Get on a region that we KNOW does not exist on the server. ----
        const string missingRegion = "/this-region-does-not-exist";
        const string anyKey = "any-key";

        var getRequest = builder.Get(missingRegion, anyKey);

        // Dump our request structure first.
        output.WriteLine($"--- Get request (region='{missingRegion}', key='{anyKey}') ---");
        output.WriteLine($"  MessageType   = {getRequest.MessageType} ({(int)getRequest.MessageType})");
        output.WriteLine($"  TransactionId = {getRequest.TransactionId}");
        output.WriteLine($"  EarlyAck      = {getRequest.EarlyAck}");
        output.WriteLine($"  NumParts      = {getRequest.Parts.Count}");
        for (var i = 0; i < getRequest.Parts.Count; i++)
        {
            var p = getRequest.Parts[i];
            output.WriteLine(
                $"  Part[{i}] IsObject={p.IsObject} len={p.Payload.Length} " +
                $"hex={Convert.ToHexString(p.Payload.Span)}");
        }

        // Dump full encoded request frame.
        var encoded = getRequest.Encode();
        output.WriteLine($"  Encoded ({encoded.Length} bytes): {Convert.ToHexString(encoded)}");

        // ---- 2. Send and dump the reply. ----
        var reply = await connection.SendRequestAsync(getRequest, cts.Token);

        output.WriteLine("--- Reply ---");
        output.WriteLine($"  MessageType   = {reply.MessageType} ({(int)reply.MessageType})");
        output.WriteLine($"  TransactionId = {reply.TransactionId}");
        output.WriteLine($"  EarlyAck      = {reply.EarlyAck}");
        output.WriteLine($"  NumParts      = {reply.Parts.Count}");
        for (var i = 0; i < reply.Parts.Count; i++)
        {
            var p = reply.Parts[i];
            output.WriteLine(
                $"  Part[{i}] IsObject={p.IsObject} len={p.Payload.Length}");
            output.WriteLine($"    hex   = {Convert.ToHexString(p.Payload.Span)}");
            // Best-effort ASCII rendering for the readable parts.
            var ascii = new string(p.Payload.Span.ToArray()
                .Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.')
                .ToArray());
            output.WriteLine($"    ascii = {ascii}");
        }
    }

    [Fact(Skip = "Diagnostic dump used to investigate the connection-state " +
        "issue around Get on a fresh connection. Run manually by removing this " +
        "Skip when re-investigating.")]
    public async Task Dump_Get_request_bytes_for_existing_region_with_missing_key()
    {
        using var cts = new CancellationTokenSource(TestTimeout);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(config)
            .BuildServiceProvider();

        var connection = services.GetRequiredService<TcrConnection>();
        var builder = services.GetRequiredService<TcrMessageBuilder>();

        await connection.ConnectAsync(fx.LocatorHost, fx.ServerPort, cancellationToken: cts.Token);

        // Theory: Get fails on a brand-new connection because region cache
        // isn't initialised yet; warm up with a Ping first.
        await connection.PingAsync(cts.Token);
        output.WriteLine("Ping ack received; connection warm.");

        const string existingRegion = "/test";          // pre-created in fixture
        var missingKey = "diag-missing-" + Guid.NewGuid().ToString("N");

        var getRequest = builder.Get(existingRegion, missingKey);

        output.WriteLine($"--- Get request (region='{existingRegion}', key='{missingKey}') ---");
        output.WriteLine($"  NumParts={getRequest.Parts.Count}");
        for (var i = 0; i < getRequest.Parts.Count; i++)
        {
            var p = getRequest.Parts[i];
            output.WriteLine(
                $"  Part[{i}] IsObject={p.IsObject} len={p.Payload.Length} " +
                $"hex={Convert.ToHexString(p.Payload.Span)}");
        }

        var reply = await connection.SendRequestAsync(getRequest, cts.Token);

        output.WriteLine($"--- Reply ---");
        output.WriteLine($"  MessageType={reply.MessageType} ({(int)reply.MessageType})");
        output.WriteLine($"  NumParts={reply.Parts.Count}");
        for (var i = 0; i < reply.Parts.Count; i++)
        {
            var p = reply.Parts[i];
            output.WriteLine(
                $"  Part[{i}] IsObject={p.IsObject} len={p.Payload.Length}");
            output.WriteLine($"    hex={Convert.ToHexString(p.Payload.Span)}");
            var ascii = new string(p.Payload.Span.ToArray()
                .Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.')
                .ToArray());
            output.WriteLine($"    ascii={ascii}");
        }
    }
}

*/