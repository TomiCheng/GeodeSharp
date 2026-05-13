using System.Text.RegularExpressions;
using Geode.Client.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end check that the IList&lt;T&gt; serialisation path round-trips
/// against a live Apache Geode server. Exercises three layers in one
/// shot:
/// <list type="bullet">
///   <item><c>ListDataConverter</c> — DSCode 65 wire encode / decode.</item>
///   <item><c>SerializationRegistry</c> open-generic dispatch — closed
///         <c>List&lt;int&gt;</c> reaches the converter via the
///         <see cref="Type.GetGenericTypeDefinition"/> fallback.</item>
///   <item><c>TypedResultAdapter</c> — wire-canonical
///         <c>List&lt;object?&gt;</c> is reshaped into the declared
///         <c>TValue</c> form (<c>IList&lt;int&gt;</c>,
///         <c>IList&lt;IList&lt;string&gt;&gt;</c>, …).</item>
/// </list>
/// Converter / adapter unit tests already cover byte-level encoding and
/// reflection branches exhaustively; this file is the integration-level
/// proof that the pieces compose correctly against a real JVM.
/// </summary>
/// <remarks>
/// <para>
/// <b>Key range</b>: 6000s — avoids 1000s
/// (<see cref="RegionCrudIntegrationTests"/>), 2000s/3000s/4000s
/// (<see cref="ScalarRoundTripIntegrationTests"/> round-trips), 5000s
/// (<see cref="ScalarRoundTripIntegrationTests"/> B-route).
/// </para>
/// <para>
/// <b>FreshConnectionSettleDelay</b> carry-over from Phase 1.1 — same
/// 3s pacing as the rest of the integration suite. Each test opens its
/// own cache.
/// </para>
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class CollectionRoundTripIntegrationTests(GeodeFixture fx)
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
    //  Round-trips
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_int_round_trips_through_IList_typed_view()
    {
        // Declared TValue = IList<int> exercises:
        //   write side: open-generic fallback finds ListDataConverter
        //               for runtime type List<int>.
        //   read side:  ListDataConverter returns List<object?>;
        //               TypedResultAdapter materialises List<int>;
        //               cast to IList<int> succeeds.
        var (services, region, ct, cts) = await OpenAsync<int, IList<int>>();
        await using (services)
        using (cts)
        {
            const int key = 6001;
            var value = new List<int> { 1, 2, 3, 4, 5 };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.IsType<List<int>>(result);   // adapter materialises List<T>
            Assert.Equal(value, result);
        }
    }

    [Fact]
    public async Task List_string_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, IList<string>>();
        await using (services)
        using (cts)
        {
            const int key = 6002;
            var value = new List<string> { "alpha", "beta", "gamma" };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(value, result);
        }
    }

    [Fact]
    public async Task Empty_List_round_trips_as_empty_not_null()
    {
        // Empty list survives as empty: wire is [DSCode 65, len 0],
        // server stores an empty ArrayList, GetAsync returns an empty
        // IList<int> — distinct from null (which would be DSCode 41).
        var (services, region, ct, cts) = await OpenAsync<int, IList<int>>();
        await using (services)
        using (cts)
        {
            const int key = 6003;
            var value = new List<int>();

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task List_string_with_null_elements_round_trips()
    {
        // Per-element DSCode means nulls travel as DSCode.NullObj in
        // their slot; adapter materialises List<string?> with null
        // preserved at the original position.
        var (services, region, ct, cts) = await OpenAsync<int, IList<string?>>();
        await using (services)
        using (cts)
        {
            const int key = 6004;
            var value = new List<string?> { "a", null, "b", null };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(4, result!.Count);
            Assert.Equal("a", result[0]);
            Assert.Null(result[1]);
            Assert.Equal("b", result[2]);
            Assert.Null(result[3]);
        }
    }

    [Fact]
    public async Task Nested_IList_of_IList_string_round_trips()
    {
        // The marquee case — nested generic target. Adapter recurses
        // per element, so each inner IList<string> is materialised
        // independently. One wire ArrayList per nesting level, no
        // reflection-based double walk.
        var (services, region, ct, cts) = await OpenAsync<int, IList<IList<string>>>();
        await using (services)
        using (cts)
        {
            const int key = 6005;
            var value = new List<IList<string>>
            {
                new List<string> { "a", "b" },
                new List<string> { "c" },
                new List<string>(),
            };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(new[] { "a", "b" }, result[0]);
            Assert.Equal(new[] { "c" }, result[1]);
            Assert.Empty(result[2]);
        }
    }

    [Fact]
    public async Task List_int_concrete_target_materialises_List_int()
    {
        // Declared TValue = List<int> (concrete) — IsInstanceOfType
        // does NOT early-out because the wire returns List<object?>;
        // adapter still goes through the List<T> materialisation path.
        var (services, region, ct, cts) = await OpenAsync<int, List<int>>();
        await using (services)
        using (cts)
        {
            const int key = 6006;
            var value = new List<int> { 10, 20, 30 };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(value, result);
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Server-side type verification (B-route via gfsh)
    //
    //  Proves the server materialised a java.util.ArrayList from our
    //  wire bytes, not e.g. an object array that happens to round-trip
    //  symmetrically. Single B-route fact covers the encode/decode
    //  contract with the JVM; per-element types (Integer, String) are
    //  already proved by the scalar B-route tests in
    //  ScalarRoundTripIntegrationTests.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_int_lands_as_java_ArrayList_on_server()
    {
        var (services, region, ct, cts) = await OpenAsync<int, IList<int>>();
        await using (services)
        using (cts)
        {
            const int key = 6500;
            var value = new List<int> { 1, 2, 3 };

            await region.PutAsync(key, value, ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={key} --key-class=java.lang.Integer",
                ct);

            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                @"^Value Class\s*:\s*java\.util\.ArrayList\s*$");
            // gfsh prints ArrayList values as "[1,2,3]" — comma
            // without trailing space (gfsh's own formatter, not the
            // standard Java ArrayList.toString() which inserts ", ").
            // The Value Class assertion above is what proves it's a
            // real java.util.ArrayList; this assertion just checks
            // element preservation.
            AssertMultilineMatch(
                output,
                @"^Value\s*:\s*\[1,2,3\]\s*$");
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Tier B-2 follow-ups: HashSet (66) / HashMap (67) /
    //  LinkedList (10) / Stack (74). Key range 7000s.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HashSet_int_round_trips_through_ISet_typed_view()
    {
        var (services, region, ct, cts) = await OpenAsync<int, ISet<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7001;
            var value = new HashSet<int> { 1, 2, 3, 4, 5 };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.IsType<HashSet<int>>(result);
            // Set-equal comparison — wire iteration order is non-
            // deterministic (cppcache unordered_set).
            Assert.Equal(value, new HashSet<int>(result));
        }
    }

    [Fact]
    public async Task HashSet_string_with_null_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, ISet<string?>>();
        await using (services)
        using (cts)
        {
            const int key = 7002;
            var value = new HashSet<string?> { "a", null, "b" };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Contains("a", result);
            Assert.Contains(null, result);
            Assert.Contains("b", result);
        }
    }

    [Fact]
    public async Task Dictionary_int_string_round_trips_through_IDictionary_typed_view()
    {
        var (services, region, ct, cts) = await OpenAsync<int, IDictionary<int, string>>();
        await using (services)
        using (cts)
        {
            const int key = 7003;
            var value = new Dictionary<int, string>
            {
                [1] = "alpha",
                [2] = "beta",
                [3] = "gamma",
            };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.IsType<Dictionary<int, string>>(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal("alpha", result[1]);
            Assert.Equal("beta", result[2]);
            Assert.Equal("gamma", result[3]);
        }
    }

    [Fact]
    public async Task Dictionary_with_null_value_round_trips()
    {
        var (services, region, ct, cts) = await OpenAsync<int, IDictionary<int, string?>>();
        await using (services)
        using (cts)
        {
            const int key = 7004;
            var value = new Dictionary<int, string?>
            {
                [1] = "a",
                [2] = null,
                [3] = "c",
            };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal("a", result[1]);
            Assert.Null(result[2]);
            Assert.Equal("c", result[3]);
        }
    }

    [Fact]
    public async Task LinkedList_int_round_trips_preserving_head_to_tail()
    {
        // LinkedList<T> is NOT IList<T>-compatible, so callers must
        // declare LinkedList<T> directly (not IList<T>) as the typed
        // view's TValue.
        var (services, region, ct, cts) = await OpenAsync<int, LinkedList<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7005;
            var value = new LinkedList<int>();
            value.AddLast(10);
            value.AddLast(20);
            value.AddLast(30);

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(new[] { 10, 20, 30 }, result);
        }
    }

    [Fact]
    public async Task Stack_int_round_trips_preserving_push_order()
    {
        // The footgun test. Push 100, 200, 300 → top = 300. Round
        // trip must end with the same top still on top — the wire
        // reverse on write + adapter reverse on read must compose to
        // identity.
        var (services, region, ct, cts) = await OpenAsync<int, Stack<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7006;
            var value = new Stack<int>();
            value.Push(100);
            value.Push(200);
            value.Push(300);

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(3, result!.Count);
            Assert.Equal(300, result.Peek());
            Assert.Equal(300, result.Pop());
            Assert.Equal(200, result.Pop());
            Assert.Equal(100, result.Pop());
        }
    }

    [Fact]
    public async Task Nested_Dictionary_of_IList_round_trips()
    {
        // IDictionary<int, IList<int>> — value-side recursion through
        // the adapter; each value flows ListDataConverter then
        // ConvertToList.
        var (services, region, ct, cts)
            = await OpenAsync<int, IDictionary<int, IList<int>>>();
        await using (services)
        using (cts)
        {
            const int key = 7007;
            var value = new Dictionary<int, IList<int>>
            {
                [1] = new List<int> { 10, 20 },
                [2] = new List<int> { 30 },
            };

            await region.PutAsync(key, value, ct);
            var result = await region.GetAsync(key, ct);

            Assert.NotNull(result);
            Assert.Equal(2, result!.Count);
            Assert.Equal(new[] { 10, 20 }, result[1]);
            Assert.Equal(new[] { 30 }, result[2]);
        }
    }

    // ── B-route: server-side type verification for each new DSCode ─

    [Fact]
    public async Task HashSet_int_lands_as_java_HashSet_on_server()
    {
        var (services, region, ct, cts) = await OpenAsync<int, ISet<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7500;
            await region.PutAsync(key, new HashSet<int> { 1 }, ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={key} --key-class=java.lang.Integer",
                ct);

            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                @"^Value Class\s*:\s*java\.util\.HashSet\s*$");
            // Single-element set has deterministic value rendering;
            // multi-element HashSet ordering is non-deterministic on
            // Java side, so we keep the value assertion to size 1.
            AssertMultilineMatch(output, @"^Value\s*:\s*\[1\]\s*$");
        }
    }

    [Fact]
    public async Task Dictionary_int_string_lands_as_java_HashMap_on_server()
    {
        var (services, region, ct, cts) = await OpenAsync<int, IDictionary<int, string>>();
        await using (services)
        using (cts)
        {
            const int key = 7501;
            await region.PutAsync(
                key,
                new Dictionary<int, string> { [42] = "answer" },
                ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={key} --key-class=java.lang.Integer",
                ct);

            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                @"^Value Class\s*:\s*java\.util\.HashMap\s*$");
            // gfsh prints HashMap with a JSON-like formatter:
            // `{"key":"value"}` with double quotes around BOTH keys
            // and values (regardless of their Java types — even an
            // Integer key gets quoted). Not the standard Java
            // HashMap.toString() form `{key=value}`. Single-entry
            // map is the only deterministic case; multi-entry order
            // depends on Java's bucket hashing.
            AssertMultilineMatch(output, @"^Value\s*:\s*\{""42"":""answer""\}\s*$");
        }
    }

    [Fact]
    public async Task LinkedList_int_lands_as_java_LinkedList_on_server()
    {
        var (services, region, ct, cts) = await OpenAsync<int, LinkedList<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7502;
            var value = new LinkedList<int>();
            value.AddLast(1);
            value.AddLast(2);
            value.AddLast(3);

            await region.PutAsync(key, value, ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={key} --key-class=java.lang.Integer",
                ct);

            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                @"^Value Class\s*:\s*java\.util\.LinkedList\s*$");
            AssertMultilineMatch(output, @"^Value\s*:\s*\[1,2,3\]\s*$");
        }
    }

    [Fact]
    public async Task Stack_int_lands_as_java_Stack_on_server()
    {
        // Push 1, 2, 3 → top=3. Server's java.util.Stack toString
        // iterates bottom→top via inherited Vector behaviour, so we
        // expect "[1,2,3]" on the wire (gfsh no-space variant). Any
        // other order would indicate a reverse-on-write bug.
        var (services, region, ct, cts) = await OpenAsync<int, Stack<int>>();
        await using (services)
        using (cts)
        {
            const int key = 7503;
            var value = new Stack<int>();
            value.Push(1);
            value.Push(2);
            value.Push(3);

            await region.PutAsync(key, value, ct);

            var output = await fx.GfshAsync(
                $"get --region=/test --key={key} --key-class=java.lang.Integer",
                ct);

            AssertMultilineMatch(output, @"^Result\s*:\s*true\s*$");
            AssertMultilineMatch(
                output,
                @"^Value Class\s*:\s*java\.util\.Stack\s*$");
            AssertMultilineMatch(output, @"^Value\s*:\s*\[1,2,3\]\s*$");
        }
    }

    private static void AssertMultilineMatch(string output, string pattern)
    {
        if (!Regex.IsMatch(output, pattern, RegexOptions.Multiline))
        {
            Assert.Fail(
                $"Pattern '{pattern}' not found in gfsh output.\n"
                + $"----- gfsh stdout -----\n{output}\n----- end -----");
        }
    }
}
