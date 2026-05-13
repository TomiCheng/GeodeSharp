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
    public void Write_within_depth_budget_succeeds()
    {
        // MaxDepth=3 leaves room for depths 0, 1, 2. List<List<int>>
        // uses depth 0 (outer), 1 (inner List), 2 (int element) — all
        // strictly less than 3.
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 3);
        var value = new List<List<int>> { new() { 1, 2 } };
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);

        registry.WriteObject(writer, value);

        Assert.NotEmpty(buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Write_exceeding_max_depth_throws_InvalidOperationException()
    {
        // MaxDepth=2: List<List<int>> hits depth=2 when the int element
        // tries to enter the registry (2 >= 2 → fail). Caller bug
        // (cycle / pathological graph) → InvalidOperationException
        // rather than GeodeException.
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 2);
        var value = new List<List<int>> { new() { 1 } };
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.WriteObject(writer, value));
        Assert.Contains("MaxDepth", ex.Message);
        Assert.Contains("2", ex.Message);    // the configured limit
    }

    [Fact]
    public void Write_top_level_scalar_at_max_depth_one_succeeds()
    {
        // MaxDepth=1 admits exactly one entry: the top-level call at
        // depth 0. Scalars don't recurse, so 0 >= 1 is false and the
        // write completes.
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);

        registry.WriteObject(writer, 42);

        Assert.NotEmpty(buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Write_any_container_at_max_depth_one_throws()
    {
        // MaxDepth=1: even a flat List<int> fails because each element
        // re-enters the registry at depth 1 (1 >= 1).
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 1);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);

        Assert.Throws<InvalidOperationException>(
            () => registry.WriteObject(writer, new List<int> { 1 }));
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
    public void Encode_then_decode_round_trips_at_the_exact_limit()
    {
        // MaxDepth=3, write List<List<int>>{ {7} }, read it back —
        // both directions hit max depth=2 (int element entry), which
        // is still allowed.
        var registry = SerializationTestHelpers.CreateRegistry(maxDepth: 3);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);
        registry.WriteObject(writer, new List<List<int>> { new() { 7 } });

        var reader = new BigEndianBinaryReader(buffer.WrittenSpan.ToArray());
        var result = registry.ReadObject(reader);

        var outer = Assert.IsType<List<object?>>(result);
        var inner = Assert.IsType<List<object?>>(outer[0]);
        Assert.Equal(7, inner[0]);
    }
}
