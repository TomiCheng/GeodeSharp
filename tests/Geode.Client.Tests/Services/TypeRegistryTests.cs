using Geode.Client.Options;
using Geode.Client.Pdx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Geode.Client.Tests.Services;

/// <summary>
/// Behaviour tests for <see cref="ITypeRegistry.RegisterPdxType{T}"/> /
/// <see cref="ITypeRegistry.RegisterPdxSerializer{T}"/> — the two
/// entry points on the per-cache PDX type registry.
/// </summary>
public class TypeRegistryTests
{
    public record TestOrder : IPdxSerializable<TestOrder>
    {
        public void ToData(IPdxWriter writer) { }
        public static TestOrder FromData(IPdxReader reader) => new();
    }

    public record TestCustomer : IPdxSerializable<TestCustomer>
    {
        public void ToData(IPdxWriter writer) { }
        public static TestCustomer FromData(IPdxReader reader) => new();
    }

    public sealed class TestOrderSerializer : IPdxSerializer<TestOrder>
    {
        public void ToData(TestOrder obj, IPdxWriter writer) { }
        public TestOrder FromData(IPdxReader reader) => new();
    }

    private static void MinimalPool(GeodeClientOptions opt) =>
        opt.Cache = new CacheOptions
        {
            Pools =
            {
                new CachePoolOptions
                {
                    Name = "test",
                    Servers = { new CacheHostPortOptions { Host = "localhost", Port = 40404 } },
                },
            },
        };

    private static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeClient(MinimalPool);
        return services.BuildServiceProvider();
    }

    private static ITypeRegistry CreateRegistry(ServiceProvider sp) =>
        sp.GetRequiredService<IGeodeCacheFactory>().Create().TypeRegistry;

    // ── RegisterPdxType ──────────────────────────────────────────

    [Fact]
    public async Task RegisterPdxType_Default_Succeeds()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxType<TestOrder>();
    }

    [Fact]
    public async Task RegisterPdxType_CustomClassName_Succeeds()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxType<TestOrder>("com.example.Order");
    }

    [Fact]
    public async Task RegisterPdxType_Duplicate_Throws_InvalidOperation()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxType<TestOrder>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterPdxType<TestOrder>());
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public async Task RegisterPdxType_DifferentTypes_BothSucceed()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxType<TestOrder>();
        registry.RegisterPdxType<TestCustomer>();
    }

    // ── RegisterPdxSerializer ───────────────────────────────────

    [Fact]
    public async Task RegisterPdxSerializer_Default_Succeeds()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxSerializer(new TestOrderSerializer());
    }

    [Fact]
    public async Task RegisterPdxSerializer_NullSerializer_Throws_ArgumentNull()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterPdxSerializer<TestOrder>(null!));
    }

    [Fact]
    public async Task RegisterPdxSerializer_Duplicate_Throws_InvalidOperation()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxSerializer(new TestOrderSerializer());
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterPdxSerializer(new TestOrderSerializer()));
        Assert.Contains("already registered", ex.Message);
    }

    // ── Cross-method collision ──────────────────────────────────

    [Fact]
    public async Task Register_IntrusiveThenExternal_SameType_Throws()
    {
        await using var sp = BuildSp();
        var registry = CreateRegistry(sp);

        registry.RegisterPdxType<TestOrder>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterPdxSerializer(new TestOrderSerializer()));
        Assert.Contains("already registered", ex.Message);
    }
}
