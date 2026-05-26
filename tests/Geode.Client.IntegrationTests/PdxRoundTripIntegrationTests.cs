using Geode.Client.Internal;
using Geode.Client.Pdx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// End-to-end PDX Put + Get round-trip against a real Geode server, using
/// a user type that implements <see cref="IPdxSerializable{TSelf}"/> for
/// every primitive <see cref="IPdxWriter"/>/<see cref="IPdxReader"/>
/// supports today (10 fields — see <see cref="AllPrimitivesPdx"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 2.1 walking-skeleton — currently <c>Skip</c>ped.</b> The write
/// path is blocked at
/// <c>PdxTypeRegistry.GetPdxIdForTypeAsync</c>
/// (<c>src/Geode.Client/Protocol/Serialization/PdxTypeRegistry.cs:150-200</c>)
/// — the <c>GET_PDX_ID_FOR_TYPE</c> wire op is still NIE — and the read
/// path is blocked at <c>SerializationRegistry.ReadPdx</c>
/// (<c>SerializationRegistry.cs:299</c>). Un-skip the <c>[Fact]</c> once
/// both land; see PROGRESS2.md Phase 2.1.
/// </para>
/// <para>
/// Key range 7000s — clear of 6000s (Collection), 8000s (PutGet), 9000s
/// (ContainsKey).
/// </para>
/// </remarks>
[Collection(nameof(GeodeCollection))]
public class PdxRoundTripIntegrationTests(GeodeFixture fx)
{
    private const string RegionName = "test";

    /// <summary>See <c>geode-fresh-conn-race.md</c> — 3s buffer for ClientHealthMonitor.</summary>
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    private async Task<IRegion<int, AllPrimitivesPdx>> BuildRegionAsync(
        ServiceProvider sp, CancellationToken ct)
    {
        // Downcast to the internal GeodeCache to reach TypeRegistry —
        // IGeodeCache.TypeRegistry is not yet exposed publicly
        // (IGeodeCache.cs:15-16 is commented for Phase 2). Once the
        // public surface lands, remove the cast.
        var cache = (GeodeCache)await sp.GetRequiredService<IGeodeCacheFactory>()
            .CreateAsync("c", ct);
        cache.TypeRegistry.RegisterPdxType<AllPrimitivesPdx>();

        await cache.PoolManager.CreateFactory()
            .AddServer(fx.LocatorHost, fx.ServerPort)
            .SetMinConnections(1)
            .BuildAsync("p", ct);

        await Task.Delay(FreshConnectionSettleDelay, ct);

        return await cache.CreateRegionFactory(RegionShortcut.Proxy)
            .CreateAsync<int, AllPrimitivesPdx>(RegionName, ct);
    }

    [Fact(Skip = "Phase 2.1 walking-skeleton: write path lands end-to-end, " +
        "but read path still NIE — SerializationRegistry has no DSCode.PDX " +
        "(93) decoder. Un-skip once PDX read lands. See PROGRESS2.md Phase 2.1.")]
    public async Task PutThenGet_AllPrimitives_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        const int key = 7001;

        var original = AllPrimitivesPdx.Sample();

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        try
        {
            await region.PutAsync(key, original, ct);
            var got = await region.GetAsync(key, ct);

            Assert.Equal(original, got);
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key} --key-class=java.lang.Integer",
                ct);
        }
    }

    /// <summary>
    /// Active smoke for the write-only path: two consecutive PUTs of the same
    /// PDX type exercise <b>Step A</b> (first time — registers schema via
    /// <c>GET_PDX_ID_FOR_TYPE</c> wire op + <c>PdxWriterWithTypeCollector</c>)
    /// and then <b>Step B</b> (cached schema — uses <c>PdxRemoteWriter</c>,
    /// no wire op for typeId). No GET / Assert until the read path lands.
    /// </summary>
    [Fact]
    public async Task PutTwice_AllPrimitives_StepBUsesCachedSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        const int key1 = 7011;
        const int key2 = 7012;

        var original = AllPrimitivesPdx.Sample();

        await using var sp = BuildSp();
        var region = await BuildRegionAsync(sp, ct);

        try
        {
            await region.PutAsync(key1, original, ct);  // Step A — registers schema
            await region.PutAsync(key2, original, ct);  // Step B — cached schema (PdxRemoteWriter)
        }
        finally
        {
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key1} --key-class=java.lang.Integer",
                ct);
            await fx.GfshAsync(
                $"remove --region=/{RegionName} --key={key2} --key-class=java.lang.Integer",
                ct);
        }
    }
}

/// <summary>
/// PDX type that exercises every primitive currently exposed on
/// <see cref="IPdxWriter"/> / <see cref="IPdxReader"/>. The record's
/// value-equality semantics give the round-trip assertion for free.
/// </summary>
/// <remarks>
/// <b>TODO Phase 2.x — fields still missing on the writer surface:</b>
/// <list type="bullet">
///   <item><c>bool[]</c>, <c>sbyte[]</c>, <c>char[]</c>, <c>short[]</c>,
///   <c>int[]</c>, <c>long[]</c>, <c>float[]</c>, <c>double[]</c>,
///   <c>string[]</c>, <c>DateTime[]</c> — PdxFieldType enum reserves
///   slots 11-19 for these; IPdxWriter has no <c>WriteXxxArray</c> method
///   yet. Same for IPdxReader.</item>
///   <item><c>byte[]</c> (raw bytes, PdxFieldType.ByteArray=13) and
///   <c>byte[][]</c> (ArrayOfByteArrays=21).</item>
///   <item><c>decimal</c> — Java <c>BigDecimal</c>; not in PdxFieldType.
///   Wire path is Phase 2.x (custom PDX serializer or a dedicated DSCode).</item>
///   <item>Nested objects via <c>WriteObject</c> / <c>WriteObjectArray</c>
///   (PdxFieldType.Object=11.5/20) — schema-level support not yet there.</item>
///   <item><c>Guid</c>, <c>TimeSpan</c> — no Java analogue at the PDX
///   layer; convention is to round-trip as <c>string</c> / <c>long</c>.</item>
/// </list>
/// When the writer gains array methods, add the corresponding fields here
/// and extend <see cref="Sample"/>.
/// </remarks>
internal sealed record AllPrimitivesPdx(
    bool BoolValue,
    sbyte SByteValue,
    char CharValue,
    short ShortValue,
    int IntValue,
    long LongValue,
    float FloatValue,
    double DoubleValue,
    string? StringValue,
    DateTime DateValue) : IPdxSerializable<AllPrimitivesPdx>
{
    public void ToData(IPdxWriter writer) =>
        writer
            .WriteBoolean(nameof(BoolValue), BoolValue)
            .WriteByte(nameof(SByteValue), SByteValue)
            .WriteChar(nameof(CharValue), CharValue)
            .WriteShort(nameof(ShortValue), ShortValue)
            .WriteInt(nameof(IntValue), IntValue)
            .WriteLong(nameof(LongValue), LongValue)
            .WriteFloat(nameof(FloatValue), FloatValue)
            .WriteDouble(nameof(DoubleValue), DoubleValue)
            .WriteString(nameof(StringValue), StringValue)
            .WriteDate(nameof(DateValue), DateValue);

    public static AllPrimitivesPdx FromData(IPdxReader reader) =>
        new(
            BoolValue: reader.ReadBoolean(nameof(BoolValue)),
            SByteValue: reader.ReadByte(nameof(SByteValue)),
            CharValue: reader.ReadChar(nameof(CharValue)),
            ShortValue: reader.ReadShort(nameof(ShortValue)),
            IntValue: reader.ReadInt(nameof(IntValue)),
            LongValue: reader.ReadLong(nameof(LongValue)),
            FloatValue: reader.ReadFloat(nameof(FloatValue)),
            DoubleValue: reader.ReadDouble(nameof(DoubleValue)),
            StringValue: reader.ReadString(nameof(StringValue)),
            DateValue: reader.ReadDate(nameof(DateValue)));

    /// <summary>Picks distinctive values per type so a wire-encoding bug in any one field surfaces in the round-trip assertion.</summary>
    public static AllPrimitivesPdx Sample() =>
        new(
            BoolValue: true,
            SByteValue: -42,
            CharValue: 'Z',
            ShortValue: -32000,
            IntValue: 2_147_483_000,
            LongValue: 9_000_000_000_000L,
            FloatValue: 3.14f,
            DoubleValue: 2.718281828459045,
            StringValue: "PDX round-trip",
            DateValue: new DateTime(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc));
}
