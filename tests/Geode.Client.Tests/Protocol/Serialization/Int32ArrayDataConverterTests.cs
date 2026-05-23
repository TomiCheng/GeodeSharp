/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class Int32ArrayDataConverterTests
{
    [Fact]
    public void Encode_empty_array_writes_dscode_and_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableInt32Array, 0x00 },
            SerializationTestHelpers.Encode(Array.Empty<int>()));
    }

    [Fact]
    public void Encode_writes_i32_be_per_element()
    {
        // {0x01020304, -1} → length-2 + 0x01 0x02 0x03 0x04 / 0xFF 0xFF 0xFF 0xFF.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableInt32Array, 0x02,
                0x01, 0x02, 0x03, 0x04,
                0xFF, 0xFF, 0xFF, 0xFF,
            },
            SerializationTestHelpers.Encode(new[] { 0x01020304, -1 }));
    }

    [Fact]
    public void Decode_reads_signed_values()
    {
        var result = (int[])SerializationTestHelpers.Decode(
            new byte[]
            {
                DSCode.CacheableInt32Array, 0x02,
                0x80, 0x00, 0x00, 0x00,                 // int.MinValue
                0x7F, 0xFF, 0xFF, 0xFF,                 // int.MaxValue
            })!;
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, result);
    }

    [Fact]
    public void RoundTrip_empty()
    {
        var result = SerializationTestHelpers.RoundTrip(Array.Empty<int>());
        Assert.Equal(Array.Empty<int>(), result);
    }

    [Theory]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 1, 2, 3 })]
    [InlineData(new[] { int.MinValue, -1, 0, 1, int.MaxValue })]
    public void RoundTrip(int[] value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    // ── VL length boundaries ────────────────────────────────────
    // Exercised here (Int32Array) instead of every array type — the
    // WriteArrayLen / ReadArrayLen path is shared with every other
    // primitive array converter, so one boundary sweep suffices.

    [Fact]
    public void RoundTrip_252_element_boundary()
    {
        // 252 = highest 1-byte VL length.
        var value = Enumerable.Range(0, 252).ToArray();
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_253_element_boundary()
    {
        // 253 = first length forcing the 3-byte VL prefix (0xFE + u16).
        var value = Enumerable.Range(0, 253).ToArray();
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_65536_element_boundary()
    {
        // 65536 = first length forcing the 5-byte VL prefix (0xFD + i32).
        var value = Enumerable.Range(0, 65536).ToArray();
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }
}

*/