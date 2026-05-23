/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class StringDataConverterTests
{
    // ────────────────────────────────────────────────────────────
    //  DSCode 87 — CacheableASCIIString (u16 char-count + ASCII bytes)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_empty_string_writes_ascii_short_with_zero_length()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableASCIIString, 0x00, 0x00 },
            SerializationTestHelpers.Encode(""));
    }

    [Fact]
    public void Encode_single_ascii_char_writes_ascii_short()
    {
        Assert.Equal(
            new byte[] { DSCode.CacheableASCIIString, 0x00, 0x01, 0x41 },
            SerializationTestHelpers.Encode("A"));
    }

    [Fact]
    public void Encode_pure_ascii_string_writes_ascii_short()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableASCIIString,
                0x00, 0x05,
                (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o',
            },
            SerializationTestHelpers.Encode("Hello"));
    }

    // ────────────────────────────────────────────────────────────
    //  DSCode 42 — CacheableString (u16 byte-count + modified UTF-8)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_non_ascii_picks_modified_utf8_short()
    {
        // 'é' = U+00E9 → 2-byte modified UTF-8: 0xC3 0xA9.
        Assert.Equal(
            new byte[] { DSCode.CacheableString, 0x00, 0x02, 0xC3, 0xA9 },
            SerializationTestHelpers.Encode("é"));
    }

    [Fact]
    public void Encode_cjk_char_uses_three_byte_modified_utf8()
    {
        // '中' = U+4E2D → 3-byte modified UTF-8: 0xE4 0xB8 0xAD.
        Assert.Equal(
            new byte[] { DSCode.CacheableString, 0x00, 0x03, 0xE4, 0xB8, 0xAD },
            SerializationTestHelpers.Encode("中"));
    }

    [Fact]
    public void Encode_embedded_nul_uses_two_byte_modified_utf8()
    {
        // \0 in modified UTF-8 is 0xC0 0x80 (NOT 0x00) — the
        // distinguishing feature from standard UTF-8.
        Assert.Equal(
            new byte[] { DSCode.CacheableString, 0x00, 0x02, 0xC0, 0x80 },
            SerializationTestHelpers.Encode("\0"));
    }

    [Fact]
    public void Encode_mixed_ascii_and_non_ascii_uses_modified_utf8()
    {
        // 'A' = 0x41 (1 byte), 'é' = 0xC3 0xA9 (2 bytes). Total 3 bytes.
        Assert.Equal(
            new byte[] { DSCode.CacheableString, 0x00, 0x03, 0x41, 0xC3, 0xA9 },
            SerializationTestHelpers.Encode("Aé"));
    }

    // ────────────────────────────────────────────────────────────
    //  DSCode 88 — CacheableASCIIStringHuge (u32 char-count + ASCII bytes)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_ascii_above_short_threshold_picks_ascii_huge()
    {
        // 65536 chars = boundary just above u16 max. Picked huge form.
        var value = new string('x', 65536);
        var encoded = SerializationTestHelpers.Encode(value);

        // First 5 bytes: DSCode 88 + u32 length 65536.
        Assert.Equal(DSCode.CacheableASCIIStringHuge, encoded[0]);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, encoded[1..5]);
        Assert.Equal(1 + 4 + 65536, encoded.Length);
        Assert.Equal((byte)'x', encoded[5]);
        Assert.Equal((byte)'x', encoded[^1]);
    }

    // ────────────────────────────────────────────────────────────
    //  DSCode 89 — CacheableStringHuge (u32 char-count + UTF-16 BE)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Encode_non_ascii_above_utf8_threshold_picks_utf16_huge()
    {
        // 35000 × 'é' = 70000 modified-UTF-8 bytes > 65535 → picks
        // DSCode 89 (UTF-16 BE). NOT 2-byte UTF-8 huge — cppcache
        // switches encoding at this point.
        var value = new string('é', 35000);
        var encoded = SerializationTestHelpers.Encode(value);

        Assert.Equal(DSCode.CacheableStringHuge, encoded[0]);
        // u32 length is char count (35000 = 0x88B8), not byte count.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x88, 0xB8 }, encoded[1..5]);
        Assert.Equal(1 + 4 + 35000 * 2, encoded.Length);
        // Each 'é' = U+00E9 written as UTF-16 BE: 0x00 0xE9.
        Assert.Equal((byte)0x00, encoded[5]);
        Assert.Equal((byte)0xE9, encoded[6]);
    }

    // ────────────────────────────────────────────────────────────
    //  DSCode 69 — CacheableNullString (decode-only)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Decode_null_string_sentinel_returns_null()
    {
        // cppcache writes 69 for null in typed-string slots; we
        // intercept null with 41 (NullObj) on write but tolerate 69
        // on read for server-compat. Payload is the bare DSCode byte.
        var result = SerializationTestHelpers.Decode(
            new byte[] { DSCode.CacheableNullString });
        Assert.Null(result);
    }

    // ────────────────────────────────────────────────────────────
    //  Round-trips
    // ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Hello, World")]
    [InlineData("é")]
    [InlineData("中文測試")]
    [InlineData("\0")]
    [InlineData("\0middle\0end")]
    [InlineData("mixed Aé中")]
    [InlineData("￿")]     // max BMP char
    public void RoundTrip(string value) =>
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));

    [Fact]
    public void RoundTrip_unpaired_high_surrogate()
    {
        // 0xD800..0xDBFF is the high-surrogate range. Each surrogate
        // half encodes as a 3-byte modified-UTF-8 sequence and must
        // round-trip intact.
        Assert.Equal("\uD800", SerializationTestHelpers.RoundTrip("\uD800"));
    }

    [Fact]
    public void RoundTrip_unpaired_low_surrogate()
    {
        Assert.Equal("\uDFFF", SerializationTestHelpers.RoundTrip("\uDFFF"));
    }

    [Fact]
    public void RoundTrip_huge_ascii()
    {
        var value = new string('a', 70000);
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_huge_utf16()
    {
        // Force the UTF-16 huge path: 35000 × non-ASCII = 70000 mod
        // UTF-8 bytes which exceeds u16, so encoding switches to UTF-16.
        var value = new string('中', 35000);
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
    }

    [Fact]
    public void RoundTrip_surrogate_pair_via_modified_utf8()
    {
        // U+1F600 (😀) encoded in .NET as two UTF-16 code units
        // (D83D, DE00). Modified UTF-8 encodes each surrogate half
        // independently as 3 bytes → 6 bytes total, round-trips intact.
        var value = "😀";
        Assert.Equal(value, SerializationTestHelpers.RoundTrip(value));
        Assert.Equal(2, value.Length);  // sanity: two UTF-16 code units
    }
}

*/