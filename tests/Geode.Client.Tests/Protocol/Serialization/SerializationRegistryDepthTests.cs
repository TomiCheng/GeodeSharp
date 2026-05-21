using System.Buffers;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Depth-limit unit tests for <see cref="SerializationRegistry"/>. The
/// limit defends both the read and write paths from stack-overflow DoS
/// via deeply nested wire payloads; same threat model and default
/// (<c>64</c>) as <see cref="System.Text.Json.JsonSerializerOptions.MaxDepth"/>.
/// </summary>
public class SerializationRegistryDepthTests
{
    [Fact]
    public void MaxDepth_default_is_64()
    {
        var registry = SerializationTestHelpers.CreateRegistry();
        Assert.Equal(64, registry.MaxDepth);
    }

    // ── Write side ─────────────────────────────────────────────

    [Fact]
    public async Task Write_within_depth_budget_succeeds()
    {
        // MaxDepth=3 leaves room for depths 0, 1, 2. List<List<int>>
        // uses depth 0 (outer), 1 (inner List), 2 (int element) — all
        // strictly less than 3.
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 3);
        var value = new List<List<int>> { new() { 1, 2 } };
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await registry.WriteObjectAsync(writer, value, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task Write_exceeding_max_depth_throws_InvalidOperationException()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 2);
        var value = new List<List<int>> { new() { 1 } };
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, value, ct: TestContext.Current.CancellationToken));
        Assert.Contains("MaxDepth", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public async Task Write_top_level_scalar_at_max_depth_one_succeeds()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await registry.WriteObjectAsync(writer, 42, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task Write_any_container_at_max_depth_one_throws()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, new List<int> { 1 }, ct: TestContext.Current.CancellationToken));
    }

    // ── Read side ──────────────────────────────────────────────

    [Fact]
    public void Read_within_depth_budget_succeeds()
    {
        // Hand-built wire: List<List<int>>{ { 7 } }. Reader uses
        // depths 0 (outer), 1 (inner), 2 (int) — fits MaxDepth=3.
        var wire = new byte[]
        {
            DSCode.CacheableArrayList, 0x01,
            DSCode.CacheableArrayList, 0x01,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 3);
        var reader = new BigEndianBinaryReader(wire);

        var result = registry.ReadObject(reader);

        var outer = Assert.IsType<List<object?>>(result);
        Assert.Single(outer);
        var inner = Assert.IsType<List<object?>>(outer[0]);
        Assert.Single(inner);
        Assert.Equal(7, inner[0]);
    }

    [Fact]
    public void Read_exceeding_max_depth_throws_GeodeException()
    {
        // Same nested-2 payload but MaxDepth=2 — the int element entry
        // at depth=2 fails (2 >= 2). Wire-level error → GeodeException
        // (the server / wire produced something we refuse to consume).
        var wire = new byte[]
        {
            DSCode.CacheableArrayList, 0x01,
            DSCode.CacheableArrayList, 0x01,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 2);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(
            () => registry.ReadObject(reader));
        Assert.Contains("MaxDepth", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    [Fact]
    public void Read_top_level_scalar_at_max_depth_one_succeeds()
    {
        var wire = new byte[] { DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x2A };
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        var reader = new BigEndianBinaryReader(wire);

        Assert.Equal(42, registry.ReadObject(reader));
    }

    [Fact]
    public void Read_any_container_at_max_depth_one_throws()
    {
        // MaxDepth=1: wire [65, 1, 57, 0,0,0,7] = List<int>{ 7 }. The
        // int element entry at depth=1 fails (1 >= 1).
        var wire = new byte[]
        {
            DSCode.CacheableArrayList, 0x01,
            DSCode.CacheableInt32, 0x00, 0x00, 0x00, 0x07,
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        var reader = new BigEndianBinaryReader(wire);

        Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
    }

    // ── Symmetry: encode at the limit feeds decode at the same limit ──

    [Fact]
    public async Task Encode_then_decode_round_trips_at_the_exact_limit()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 3);

        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());
        await registry.WriteObjectAsync(writer, new List<List<int>> { new() { 7 } }, ct: TestContext.Current.CancellationToken);

        var reader = new BigEndianBinaryReader(writer.WrittenSpan.ToArray());
        var result = registry.ReadObject(reader);

        var outer = Assert.IsType<List<object?>>(result);
        var inner = Assert.IsType<List<object?>>(outer[0]);
        Assert.Equal(7, inner[0]);
    }
}
