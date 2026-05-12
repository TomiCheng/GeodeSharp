using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 1.3.0 end-to-end check that every newly-registered built-in
/// scalar / <see cref="DateTime"/> converter round-trips correctly
/// against a live Apache Geode server. The scope is "the wire layer
/// I just touched still works once the bytes reach a real JVM" —
/// converter unit tests already exhaustively cover the byte-level
/// encoding; this file is the integration-level smoke check on top.
///
/// <para>
/// One <see cref="IRegion{TKey,TValue}"/> typed view per value type,
/// each test does Put → Get and asserts equality. Keys live in
/// distinct ranges so the tests can run in any order against the
/// same shared <see cref="GeodeFixture"/> region.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Key range</b>: 2000s in <c>int</c> column (this file) — picked
/// to avoid collision with
/// <see cref="RegionCrudIntegrationTests"/>'s 1000s and 0x7FFF_xxxx
/// ranges. The <c>long</c>-key test uses a value outside the
/// <c>int</c> range entirely.
/// </para>
/// <para>
/// <b>FreshConnectionSettleDelay carry-over</b> from Phase 1.1 — see
/// <see cref="RegionCrudIntegrationTests"/> for context. Each test
/// opens its own cache and pays the 3s delay; total file run-time is
/// dominated by that, not by the wire ops.
/// </para>
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class ScalarRoundTripIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

    private const string RegionName = "test";

    private void ConfigureCacheXml(GeodeClientOptions config)
    {
        config.CacheXml = new CacheXmlOptions
        {
            Pools =
            {
                new CacheXmlPoolOptions
                {
                    Name = "testPool",
                    Servers =
                    {
                        new CacheXmlHostPort
                        {
                            Host = fx.LocatorHost,
                            Port = fx.ServerPort,
                        },
                    },
                },
            },
            Regions =
            {
                new CacheXmlRegionOptions
                {
                    Name = RegionName,
                    Attributes = { PoolName = "testPool" },
                },
            },
        };
    }

    private async Task<(ServiceProvider Services, IRegion<TKey, TValue> Region, CancellationToken Ct, CancellationTokenSource Cts)>
        OpenAsync<TKey, TValue>()
        where TKey : IEquatable<TKey>
    {
        var cts = new CancellationTokenSource(TestTimeout);

        var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCacheXml)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCache>();
        await cache.EnsureInitializedAsync(cts.Token);

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<TKey, TValue>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    // ────────────────────────────────────────────────────────────
    //  Value-side round-trips (int key, varying value type)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bool_value_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, bool>();
        await using (services)
        using (cts)
        {
            const int key = 2001;

            await region.PutAsync(key, true, ct);
            Assert.True(await region.GetAsync(key, ct));

            await region.PutAsync(key, false, ct);
            Assert.False(await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Char_value_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, char>();
        await using (services)
        using (cts)
        {
            const int key = 2002;

            // CJK char to exercise the full UTF-16 code-unit width.
            await region.PutAsync(key, '中', ct);
            Assert.Equal('中', await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Byte_value_round_trips_across_signed_boundary()
    {
        var (services, region, ct, cts) = await OpenAsync<int, byte>();
        await using (services)
        using (cts)
        {
            const int key = 2003;

            // 0x80 = .NET 128 = Java -128: the boundary where signed /
            // unsigned interpretations diverge. Wire bit-pattern must
            // survive intact regardless.
            await region.PutAsync(key, (byte)0x80, ct);
            Assert.Equal((byte)0x80, await region.GetAsync(key, ct));

            // And 0xFF (.NET 255, Java -1) for completeness.
            await region.PutAsync(key, (byte)0xFF, ct);
            Assert.Equal((byte)0xFF, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Int16_value_round_trips_including_negative()
    {
        var (services, region, ct, cts) = await OpenAsync<int, short>();
        await using (services)
        using (cts)
        {
            const int key = 2004;

            await region.PutAsync(key, short.MinValue, ct);
            Assert.Equal(short.MinValue, await region.GetAsync(key, ct));

            await region.PutAsync(key, short.MaxValue, ct);
            Assert.Equal(short.MaxValue, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Int64_value_round_trips_including_negative()
    {
        var (services, region, ct, cts) = await OpenAsync<int, long>();
        await using (services)
        using (cts)
        {
            const int key = 2005;

            await region.PutAsync(key, long.MinValue, ct);
            Assert.Equal(long.MinValue, await region.GetAsync(key, ct));

            await region.PutAsync(key, long.MaxValue, ct);
            Assert.Equal(long.MaxValue, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Single_value_round_trips_including_nan_and_infinity()
    {
        var (services, region, ct, cts) = await OpenAsync<int, float>();
        await using (services)
        using (cts)
        {
            const int key = 2006;

            await region.PutAsync(key, 3.14159f, ct);
            Assert.Equal(3.14159f, await region.GetAsync(key, ct));

            await region.PutAsync(key, float.PositiveInfinity, ct);
            Assert.Equal(float.PositiveInfinity, await region.GetAsync(key, ct));

            await region.PutAsync(key, float.NaN, ct);
            Assert.True(float.IsNaN(await region.GetAsync(key, ct)));
        }
    }

    [Fact]
    public async Task Double_value_round_trips_including_nan_and_infinity()
    {
        var (services, region, ct, cts) = await OpenAsync<int, double>();
        await using (services)
        using (cts)
        {
            const int key = 2007;

            await region.PutAsync(key, Math.PI, ct);
            Assert.Equal(Math.PI, await region.GetAsync(key, ct));

            await region.PutAsync(key, double.NegativeInfinity, ct);
            Assert.Equal(double.NegativeInfinity, await region.GetAsync(key, ct));

            await region.PutAsync(key, double.NaN, ct);
            Assert.True(double.IsNaN(await region.GetAsync(key, ct)));
        }
    }

    [Fact]
    public async Task DateTime_value_round_trips_as_utc()
    {
        var (services, region, ct, cts) = await OpenAsync<int, DateTime>();
        await using (services)
        using (cts)
        {
            const int key = 2008;

            // Pick a millisecond-aligned UTC instant so encode's
            // truncate-to-ms doesn't mask a real bug.
            var input = new DateTime(2026, 5, 12, 14, 30, 45, 123, DateTimeKind.Utc);

            await region.PutAsync(key, input, ct);
            var result = await region.GetAsync(key, ct);

            Assert.Equal(input, result);
            Assert.Equal(DateTimeKind.Utc, result.Kind);
        }
    }

    // ────────────────────────────────────────────────────────────
    //  String value — exercises all four CacheableString DSCode variants
    //  (ASCII short / ASCII huge / mod-UTF-8 short / UTF-16 huge)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task String_ascii_value_round_trips()
    {
        // ASCII content + length ≤ 65535 → DSCode 87 (CacheableASCIIString).
        var (services, region, ct, cts) = await OpenAsync<int, string>();
        await using (services)
        using (cts)
        {
            const int key = 3001;
            const string value = "Hello, Geode!";

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task String_non_ascii_value_round_trips_via_modified_utf8()
    {
        // Non-ASCII content (CJK + accented) + modified-UTF-8 byte length
        // well under 65535 → DSCode 42 (CacheableString). Verifies the
        // round-trip survives \0, modified-UTF-8 encoding, and the
        // surrogate-pair handling for the emoji.
        var (services, region, ct, cts) = await OpenAsync<int, string>();
        await using (services)
        using (cts)
        {
            const int key = 3002;
            const string value = "中文 mixed Aé 你好\0 \U0001F600";

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task String_huge_ascii_value_round_trips()
    {
        // > 65535 chars, all ASCII → DSCode 88 (CacheableASCIIStringHuge).
        var (services, region, ct, cts) = await OpenAsync<int, string>();
        await using (services)
        using (cts)
        {
            const int key = 3003;
            var value = new string('x', 70000);

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task String_huge_non_ascii_value_round_trips_via_utf16()
    {
        // 35000 × '中' = 105000 modified-UTF-8 bytes > 65535 → encoding
        // switches to DSCode 89 (CacheableStringHuge, UTF-16 BE).
        var (services, region, ct, cts) = await OpenAsync<int, string>();
        await using (services)
        using (cts)
        {
            const int key = 3004;
            var value = new string('中', 35000);

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    // ────────────────────────────────────────────────────────────
    //  byte[] value — VL-encoded length (inline / u16 / i32) + raw bytes
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bytes_value_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, byte[]>();
        await using (services)
        using (cts)
        {
            const int key = 4001;
            var value = new byte[] { 0x00, 0x7F, 0x80, 0xFF, 0xDE, 0xAD, 0xBE, 0xEF };

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    [Fact]
    public async Task Bytes_empty_value_round_trips_as_empty_not_null()
    {
        // Distinct from null: byte[0] writes [46, 0x00] (DSCode + VL
        // length 0), Get returns a zero-length array, not null.
        var (services, region, ct, cts) = await OpenAsync<int, byte[]>();
        await using (services)
        using (cts)
        {
            const int key = 4002;
            var value = Array.Empty<byte>();

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task Bytes_huge_value_round_trips_via_i32_length()
    {
        // > 65535 bytes → VL length uses the 5-byte i32 prefix.
        var (services, region, ct, cts) = await OpenAsync<int, byte[]>();
        await using (services)
        using (cts)
        {
            const int key = 4003;
            var value = new byte[100000];
            new Random(42).NextBytes(value);

            await region.PutAsync(key, value, ct);
            Assert.Equal(value, await region.GetAsync(key, ct));
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Key-side round-trips (smoke check: non-int key types work on the wire)
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Long_key_round_trips_with_int_value()
    {
        var (services, region, ct, cts) = await OpenAsync<long, int>();
        await using (services)
        using (cts)
        {
            // Outside the int range to prove we're not silently truncating.
            const long key = (long)int.MaxValue + 1000;

            await region.PutAsync(key, 99, ct);
            Assert.Equal(99, await region.GetAsync(key, ct));
            Assert.True(await region.ContainsKeyAsync(key, ct));
        }
    }

    [Fact]
    public async Task String_key_round_trips_with_int_value()
    {
        // Smoke test: most common real-world Key shape (string ID).
        var (services, region, ct, cts) = await OpenAsync<string, int>();
        await using (services)
        using (cts)
        {
            const string key = "order:42-中文";

            await region.PutAsync(key, 7, ct);
            Assert.Equal(7, await region.GetAsync(key, ct));
            Assert.True(await region.ContainsKeyAsync(key, ct));
        }
    }
}
