using System.Text.RegularExpressions;
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

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        await cache.EnsureInitializedAsync(cts.Token);

        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<TKey, TValue>(RegionName);
        Assert.NotNull(region);

        return (services, region, cts.Token, cts);
    }

    // ────────────────────────────────────────────────────────────
    //  Server-side type verification (B-route: gfsh bypass read)
    //
    //  Round-trip Put/Get cannot prove the server understood our wire
    //  bytes — a symmetric encoder/decoder bug round-trips fine while
    //  the server stores garbage. These tests Put via our client, then
    //  query the server through gfsh and assert the Java class + value
    //  the server actually materialized. See PROGRESS.md Phase 1.3.0
    //  for the full rationale; this single int probe is the prototype
    //  before extending to other types.
    // ────────────────────────────────────────────────────────────

    // 5000s range reserved for B-route server-side checks so they
    // don't collide with the 2000s/3000s/4000s round-trip tests below.

    /// <summary>
    /// Puts <paramref name="value"/> under <paramref name="intKey"/>
    /// via our client, then runs gfsh <c>get</c> against the same key
    /// and asserts the server's <c>Value Class</c> is exactly
    /// <paramref name="expectedJavaClass"/> and <c>Value</c> renders as
    /// <paramref name="expectedJavaToString"/>. This proves the server
    /// deserialized the bytes we sent into the intended Java type with
    /// the intended value — a guarantee Put/Get round-trip cannot make
    /// because a symmetric encoder/decoder bug round-trips fine.
    /// </summary>
    private async Task VerifyServerSideAsync<TValue>(
        int intKey,
        TValue value,
        string expectedJavaClass,
        string expectedJavaToString)
        where TValue : notnull
    {
        var (services, region, ct, cts) = await OpenAsync<int, TValue>();
        await using (services)
        using (cts)
        {
            await region.PutAsync(intKey, value, ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={intKey} --key-class=java.lang.Integer",
                ct);

            // gfsh's `get` output looks like:
            //   Result      : true
            //   Key Class   : java.lang.Integer
            //   Key         : 5001
            //   Value Class : java.lang.Integer
            //   Value       : 42
            //
            // Tight regex matches rooted on the labels + Multiline
            // option so `$` anchors to end-of-line — without that,
            // "java.lang.Integer" appearing mid-output won't satisfy
            // `\s*$` because more lines follow.
            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                $@"^Value Class\s*:\s*{Regex.Escape(expectedJavaClass)}\s*$");
            AssertMultilineMatch(
                output,
                $@"^Value\s*:\s*{Regex.Escape(expectedJavaToString)}\s*$");
        }
    }

    /// <summary>
    /// <see cref="Assert.Matches(string, string?)"/> with
    /// <see cref="RegexOptions.Multiline"/> enabled (so <c>^</c> and
    /// <c>$</c> anchor to line boundaries, not just string boundaries)
    /// and a failure message that dumps the full gfsh output. The
    /// dump matters: when a test fails we want to see the actual
    /// tabular output once, not have to add ad-hoc Console.WriteLine
    /// and re-run.
    /// </summary>
    private static void AssertMultilineMatch(string output, string pattern)
    {
        if (!Regex.IsMatch(output, pattern, RegexOptions.Multiline))
        {
            Assert.Fail(
                $"Pattern '{pattern}' not found in gfsh output.\n"
                + $"----- gfsh stdout -----\n{output}\n----- end -----");
        }
    }

    [Fact]
    public Task Int32_value_lands_as_java_Integer_on_server()
        => VerifyServerSideAsync(5001, 42, "java.lang.Integer", "42");

    [Fact]
    public Task Boolean_value_lands_as_java_Boolean_on_server()
        => VerifyServerSideAsync(5002, true, "java.lang.Boolean", "true");

    [Fact]
    public Task Character_value_lands_as_java_Character_on_server()
        // CJK char to also smoke-test UTF-8 on the gfsh stdout path.
        // gfsh wraps both Character and String values in double quotes
        // for display (a presentation choice, not part of the value);
        // the Value Class assertion is what proves it's really a
        // java.lang.Character on the server, not a java.lang.String.
        => VerifyServerSideAsync(5003, '中', "java.lang.Character", "\"中\"");

    [Fact]
    public Task Byte_value_lands_as_java_Byte_on_server()
        // .NET byte 0x80 = 128 unsigned ↔ Java byte -128 signed.
        // gfsh prints Java's signed toString, so the assertion is "-128".
        => VerifyServerSideAsync(5004, (byte)0x80, "java.lang.Byte", "-128");

    [Fact]
    public Task Int16_value_lands_as_java_Short_on_server()
        => VerifyServerSideAsync(5005, short.MaxValue, "java.lang.Short", "32767");

    [Fact]
    public Task Int64_value_lands_as_java_Long_on_server()
        => VerifyServerSideAsync(
            5006,
            long.MaxValue,
            "java.lang.Long",
            "9223372036854775807");

    [Fact]
    public Task Single_value_lands_as_java_Float_on_server()
        // Float.toString(3.14f) in Java prints exactly "3.14".
        => VerifyServerSideAsync(5007, 3.14f, "java.lang.Float", "3.14");

    [Fact]
    public Task Double_value_lands_as_java_Double_on_server()
        // Double.toString(Math.PI) in Java prints "3.141592653589793"
        // (same 17-digit shortest-round-trip as .NET's "G17" / default).
        => VerifyServerSideAsync(
            5008,
            Math.PI,
            "java.lang.Double",
            "3.141592653589793");

    [Fact]
    public Task DateTime_value_lands_as_java_Date_on_server()
    {
        // 2026-05-12 14:30:45.123 UTC. Empirically gfsh prints
        // java.util.Date as the raw ms-since-epoch long, not as
        // Date.toString(), which is actually a stronger check than
        // EEE MMM dd HH:mm:ss zzz yyyy because it pins the
        // millisecond field too (Date.toString truncates to seconds).
        //
        //   Days 1970-01-01 .. 2026-05-12 UTC = 20585
        //   20585 * 86400 + 14*3600 + 30*60 + 45 = 1 778 596 245 s
        //   * 1000 + 123 ms = 1 778 596 245 123
        var value = new DateTime(2026, 5, 12, 14, 30, 45, 123, DateTimeKind.Utc);
        return VerifyServerSideAsync(
            5010,
            value,
            "java.util.Date",
            "1778596245123");
    }

    // ────────────────────────────────────────────────────────────
    //  String — one B-route fact per DSCode variant. Each variant is
    //  its own encoder code path (ASCII vs modified-UTF-8 short, plus
    //  the huge variants that switch to a 4-byte length + the
    //  non-ASCII huge case that swaps modified-UTF-8 out for UTF-16
    //  BE), so a single ASCII-short check would silently bless the
    //  three untested encoders.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public Task String_ascii_value_lands_as_java_String_on_server()
        // DSCode 87 (CacheableASCIIString): ASCII content + ≤65535 bytes.
        // gfsh wraps String values in double quotes for display
        // (see Character_value_lands_as_java_Character_on_server).
        => VerifyServerSideAsync(
            5009,
            "Hello, Geode!",
            "java.lang.String",
            "\"Hello, Geode!\"");

    [Fact]
    public Task String_non_ascii_value_lands_as_java_String_on_server()
        // DSCode 42 (CacheableString): non-ASCII content + modified-UTF-8
        // byte length ≤65535. Mix Latin extended + CJK to exercise
        // 2-byte and 3-byte modified-UTF-8 sequences in one go.
        => VerifyServerSideAsync(
            5011,
            "中文 mixed Aé 你好",
            "java.lang.String",
            "\"中文 mixed Aé 你好\"");

    [Fact]
    public Task String_huge_ascii_value_lands_as_java_String_on_server()
    {
        // DSCode 88 (CacheableASCIIStringHuge): ASCII + >65535 chars,
        // length-prefix switches from u16 to i32.
        var value = new string('x', 70000);
        return VerifyServerSideAsync(
            5012,
            value,
            "java.lang.String",
            "\"" + value + "\"");
    }

    [Fact]
    public Task String_huge_non_ascii_value_lands_as_java_String_on_server()
    {
        // DSCode 89 (CacheableStringHuge): non-ASCII + modified-UTF-8
        // length would exceed 65535, so cppcache deliberately switches
        // the encoding to UTF-16 BE with a u32 char-count length
        // prefix. This is the most uniquely-shaped path in the whole
        // string converter — UTF-16 BE on the wire, every other code
        // path uses modified-UTF-8.
        //
        // 35000 × '中' = 105000 modified-UTF-8 bytes (would overflow
        // u16), but only 70000 UTF-16 bytes — fits cleanly.
        var value = new string('中', 35000);
        return VerifyServerSideAsync(
            5013,
            value,
            "java.lang.String",
            "\"" + value + "\"");
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
