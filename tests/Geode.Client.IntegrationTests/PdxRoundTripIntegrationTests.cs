using Geode.Client.Options;
using Geode.Client.Pdx;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Geode.Client.IntegrationTests;

/// <summary>
/// Phase 2.1 PDX target — round-trip a class containing every PDX
/// primitive (mirrors cppcache <c>PdxWriter</c> / <c>PdxReader</c>
/// primitive surface). Fails today (wire codec not wired through
/// <c>SerializationRegistry</c> yet); landing point for the next
/// implementation step.
/// </summary>
[Collection(nameof(GeodeCollection))]
public class PdxRoundTripIntegrationTests(GeodeFixture fx)
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FreshConnectionSettleDelay = TimeSpan.FromSeconds(3);
    private const string RegionName = "test";

    /// <summary>One field per PDX primitive (cppcache <c>PdxWriter.hpp:65-176</c>).</summary>
    public record AllPrimitivesPdx(
        bool BoolVal,
        sbyte SByteVal,
        char CharVal,
        short ShortVal,
        int IntVal,
        long LongVal,
        float FloatVal,
        double DoubleVal,
        string StringVal,
        DateTime DateVal) : IPdxSerializable<AllPrimitivesPdx>
    {
        public void ToData(IPdxWriter w)
        {
            w.WriteBoolean("bool", BoolVal);
            w.WriteByte("sbyte", SByteVal);
            w.WriteChar("char", CharVal);
            w.WriteShort("short", ShortVal);
            w.WriteInt("int", IntVal);
            w.WriteLong("long", LongVal);
            w.WriteFloat("float", FloatVal);
            w.WriteDouble("double", DoubleVal);
            w.WriteString("string", StringVal);
            w.WriteDate("date", DateVal);
        }

        public static AllPrimitivesPdx FromData(IPdxReader r) => new(
            BoolVal: r.ReadBoolean("bool"),
            SByteVal: r.ReadByte("sbyte"),
            CharVal: r.ReadChar("char"),
            ShortVal: r.ReadShort("short"),
            IntVal: r.ReadInt("int"),
            LongVal: r.ReadLong("long"),
            FloatVal: r.ReadFloat("float"),
            DoubleVal: r.ReadDouble("double"),
            StringVal: r.ReadString("string")!,
            DateVal: r.ReadDate("date"));
    }

    private void ConfigureCache(GeodeClientOptions config)
    {
        config.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "testPool",
                    Servers = { new CacheHostPortOptions { Host = fx.LocatorHost, Port = fx.ServerPort } },
                },
            },
            Regions =
            {
                new CacheRegionOptions { Name = RegionName, Attributes = { PoolName = "testPool" } },
            },
        };
    }

    [Fact]
    public async Task AllPrimitives_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddGeodeClient(ConfigureCache)
            .BuildServiceProvider();

        var cache = services.GetRequiredService<IGeodeCacheFactory>().Create();
        cache.TypeRegistry.RegisterPdxType<AllPrimitivesPdx>();

        await cache.EnsureInitializedAsync(cts.Token);
        await Task.Delay(FreshConnectionSettleDelay, cts.Token);

        var region = cache.GetRegion<int, AllPrimitivesPdx>(RegionName);
        Assert.NotNull(region);

        const int key = 6001;
        var value = new AllPrimitivesPdx(
            BoolVal: true,
            SByteVal: -42,
            CharVal: 'X',
            ShortVal: -12345,
            IntVal: 2_000_000_000,
            LongVal: 9_000_000_000_000_000_000L,
            FloatVal: 3.14f,
            DoubleVal: 2.71828,
            StringVal: "hello pdx",
            DateVal: new DateTime(2026, 5, 20, 12, 34, 56, DateTimeKind.Utc));

        await region.PutAsync(key, value, cts.Token);
        var got = await region.GetAsync(key, cts.Token);

        Assert.Equal(value, got);
    }
}
