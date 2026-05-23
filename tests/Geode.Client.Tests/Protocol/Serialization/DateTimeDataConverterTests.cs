/*
using Geode.Client.Protocol;
using Xunit;

namespace Geode.Client.Tests.Protocol.Serialization;

public class DateTimeDataConverterTests
{
    [Fact]
    public void Encode_unix_epoch_writes_dscode_and_zero_ms()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDate,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(DateTime.UnixEpoch));
    }

    [Fact]
    public void Encode_one_millisecond_after_epoch()
    {
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDate,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            },
            SerializationTestHelpers.Encode(
                DateTime.UnixEpoch.AddMilliseconds(1)));
    }

    [Fact]
    public void Encode_one_millisecond_before_epoch()
    {
        // -1 ms = 0xFFFF_FFFF_FFFF_FFFF as signed i64.
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDate,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            },
            SerializationTestHelpers.Encode(
                DateTime.UnixEpoch.AddMilliseconds(-1)));
    }

    [Fact]
    public void Encode_truncates_sub_millisecond_ticks()
    {
        // 0.9 ms = 9000 ticks; should truncate (not round) to 0 ms.
        var nineTenthsOfAMs = DateTime.UnixEpoch.AddTicks(9000);
        Assert.Equal(
            new byte[]
            {
                DSCode.CacheableDate,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            SerializationTestHelpers.Encode(nineTenthsOfAMs));
    }

    [Fact]
    public void Encode_unspecified_kind_throws()
    {
        var unspecified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var ex = Assert.Throws<ArgumentException>(() =>
            SerializationTestHelpers.Encode(unspecified));
        Assert.Contains("Unspecified", ex.Message);
    }

    [Fact]
    public void Encode_local_kind_converts_to_utc()
    {
        // Pick a specific UTC instant and express it via Local — the
        // wire bytes must match the UTC representation regardless of
        // the host's local time zone offset.
        var utc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal(
            SerializationTestHelpers.Encode(utc),
            SerializationTestHelpers.Encode(local));
    }

    [Fact]
    public void Decode_returns_utc_kind()
    {
        var result = (DateTime)SerializationTestHelpers.Decode(new byte[]
        {
            DSCode.CacheableDate,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        })!;

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(DateTime.UnixEpoch, result);
    }

    [Fact]
    public void RoundTrip_utc_value_preserves_kind_and_instant()
    {
        var input = new DateTime(2026, 5, 12, 14, 30, 45, DateTimeKind.Utc);
        var result = SerializationTestHelpers.RoundTrip(input);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(input, result);
    }

    [Fact]
    public void RoundTrip_local_value_returns_same_instant_as_utc()
    {
        var utc = new DateTime(2026, 5, 12, 14, 30, 45, DateTimeKind.Utc);
        var local = utc.ToLocalTime();
        var result = SerializationTestHelpers.RoundTrip(local);

        // Read returns Utc Kind; the instant matches the original.
        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(utc, result);
    }

    [Fact]
    public void RoundTrip_pre_epoch_date()
    {
        // 1969-12-31 23:59:59 UTC — 1 second before epoch.
        var input = DateTime.UnixEpoch.AddSeconds(-1);
        var result = SerializationTestHelpers.RoundTrip(input);

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(input, result);
    }
}

*/