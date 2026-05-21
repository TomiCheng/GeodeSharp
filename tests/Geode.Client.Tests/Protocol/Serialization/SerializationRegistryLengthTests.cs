using System.Buffers;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Length-limit unit tests for the
/// <see cref="Options.SerializationOptions.MaxArrayLength"/> /
/// <see cref="Options.SerializationOptions.MaxBytesLength"/> /
/// <see cref="Options.SerializationOptions.MaxStringLength"/> trio.
/// Defends both read and write paths against pre-allocation DoS where a
/// hostile wire length-prefix would force a gigabyte-scale allocation.
/// </summary>
/// <remarks>
/// One representative converter per dispatch path: a primitive array
/// (<see cref="Int32ArrayDataConverter"/> — CacheScopeContext-direct
/// for <see cref="Options.SerializationOptions.MaxArrayLength"/>), a
/// collection (<see cref="ListDataConverter"/> — registry-snapshot
/// for the same limit), <see cref="BytesDataConverter"/> (separate
/// <see cref="Options.SerializationOptions.MaxBytesLength"/>), and
/// <see cref="StringDataConverter"/> (separate
/// <see cref="Options.SerializationOptions.MaxStringLength"/> with
/// per-DSCode branches). The pattern is identical across the other
/// converters, so per-converter exhaustive coverage would be
/// duplicative.
/// </remarks>
public class SerializationRegistryLengthTests
{
    // ── Primitive array (CacheScopeContext-direct path) ────────

    [Fact]
    public async Task Int32Array_write_at_limit_succeeds()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxArrayLength: 3);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await registry.WriteObjectAsync(writer, new[] { 1, 2, 3 }, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task Int32Array_write_over_limit_throws_InvalidOperationException()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxArrayLength: 3);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, new[] { 1, 2, 3, 4 }, ct: TestContext.Current.CancellationToken));
        Assert.Contains("MaxArrayLength", ex.Message);
        Assert.Contains("4", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void Int32Array_read_over_limit_throws_GeodeException()
    {
        // Wire claims length 4, configured limit is 3 — payload bytes
        // never get read past the length-prefix because the check
        // fires before allocation.
        var wire = new byte[]
        {
            DSCode.CacheableInt32Array, 0x04,
            // No payload — converter throws before reading any element.
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxArrayLength: 3);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxArrayLength", ex.Message);
        Assert.Contains("4", ex.Message);
    }

    // ── Collection (registry-snapshot path) ────────────────────

    [Fact]
    public async Task List_write_over_limit_throws_InvalidOperationException()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxArrayLength: 3);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, new List<int> { 1, 2, 3, 4 }, ct: TestContext.Current.CancellationToken));
        Assert.Contains("MaxArrayLength", ex.Message);
    }

    [Fact]
    public void List_read_over_limit_throws_GeodeException()
    {
        var wire = new byte[]
        {
            DSCode.CacheableArrayList, 0x04,
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxArrayLength: 3);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxArrayLength", ex.Message);
    }

    // ── byte[] (separate MaxBytesLength) ───────────────────────

    [Fact]
    public async Task Bytes_uses_MaxBytesLength_not_MaxArrayLength()
    {
        var registry = SerializationTestHelpers.CreateRegistry(
            maxArrayLength: 3,
            maxBytesLength: 10);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await registry.WriteObjectAsync(writer, new byte[] { 1, 2, 3, 4, 5 }, ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task Bytes_write_over_limit_throws_InvalidOperationException()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxBytesLength: 4);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, new byte[] { 1, 2, 3, 4, 5 }, ct: TestContext.Current.CancellationToken));
        Assert.Contains("MaxBytesLength", ex.Message);
    }

    [Fact]
    public void Bytes_read_over_limit_throws_GeodeException()
    {
        var wire = new byte[]
        {
            DSCode.CacheableBytes, 0x05,
            // payload omitted — check fires before consuming bytes
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxBytesLength: 4);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxBytesLength", ex.Message);
    }

    // ── String (MaxStringLength, multi-DSCode) ─────────────────

    [Fact]
    public async Task String_write_over_limit_throws_InvalidOperationException()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxStringLength: 3);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.WriteObjectAsync(writer, "abcd", ct: TestContext.Current.CancellationToken));
        Assert.Contains("MaxStringLength", ex.Message);
    }

    [Fact]
    public async Task String_at_limit_succeeds()
    {
        var registry = SerializationTestHelpers.CreateRegistry(maxStringLength: 3);
        using var writer = new DataOutput(SerializationTestHelpers.CreateRegistry());

        await registry.WriteObjectAsync(writer, "abc", ct: TestContext.Current.CancellationToken);

        Assert.NotEmpty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void String_read_ascii_short_over_limit_throws_GeodeException()
    {
        // DSCode 87 (CacheableASCIIString): u16 length. Wire claims
        // length 10, configured limit is 3.
        var wire = new byte[]
        {
            DSCode.CacheableASCIIString, 0x00, 0x0A,
            // payload omitted — check fires before reading chars
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxStringLength: 3);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxStringLength", ex.Message);
    }

    [Fact]
    public void String_read_ascii_huge_over_limit_throws_GeodeException()
    {
        // DSCode 88 (CacheableASCIIStringHuge): i32 length — the main
        // attack surface (can be int.MaxValue from a hostile server).
        var wire = new byte[]
        {
            DSCode.CacheableASCIIStringHuge, 0x00, 0x01, 0x00, 0x00,    // length 65536
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxStringLength: 100);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxStringLength", ex.Message);
    }

    [Fact]
    public void String_read_utf16_huge_over_limit_throws_GeodeException()
    {
        // DSCode 89 (CacheableStringHuge): i32 char-count, UTF-16 BE.
        // Same attack surface as 88.
        var wire = new byte[]
        {
            DSCode.CacheableStringHuge, 0x00, 0x01, 0x00, 0x00,    // length 65536
        };
        var registry = SerializationTestHelpers.CreateRegistry(maxStringLength: 100);
        var reader = new BigEndianBinaryReader(wire);

        var ex = Assert.Throws<GeodeException>(() => registry.ReadObject(reader));
        Assert.Contains("MaxStringLength", ex.Message);
    }

    // ── Snapshot defaults ──────────────────────────────────────

    [Fact]
    public void Defaults_match_production()
    {
        var registry = SerializationTestHelpers.CreateRegistry();

        Assert.Equal(1_000_000, registry.MaxArrayLength);
        Assert.Equal(1_000_000, registry.MaxStringLength);
        // MaxBytesLength is consumed via CacheScopeContext directly by
        // BytesDataConverter — not snapshot on the registry — so no
        // assertion here. Validator-default test covers the 10M value.
    }
}
