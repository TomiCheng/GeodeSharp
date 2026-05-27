using Geode.Client.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Geode.Client.Services;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Wire-level helpers for converter tests. Builds a minimal
/// <see cref="IServiceProvider"/> + an uninitialised
/// <see cref="GeodeCache"/> instance so tests can roundtrip values
/// through the same <see cref="SerializationRegistry"/> production uses.
/// </summary>
/// <remarks>
/// Each <see cref="Encode(object, int, int, int, int)"/> /
/// <see cref="Decode(byte[], int, int, int, int)"/> /
/// <see cref="RoundTrip{T}(T, int, int, int, int)"/> call spins up a
/// fresh SP + cache + registry. Limit-bound converters (Bytes / String /
/// every Array*) snapshot the cap into a private field at first access,
/// so callers MUST pass overrides via these helpers — mutating
/// <see cref="GeodeCache.CacheProperties"/> after the registry has
/// materialised is a no-op for the existing converter instances.
/// </remarks>
internal static class SerializationTestHelpers
{
    /// <summary>Default DI host with logging stubs + <c>AddGeodeFactory</c>.</summary>
    public static ServiceProvider BuildSp()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddGeodeFactory();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Build a <see cref="GeodeCache"/> directly via
    /// <see cref="ActivatorUtilities"/> (skipping
    /// <see cref="GeodeCache.InitializeAsync"/> — the registry only
    /// needs the cache instance + its <see cref="SystemProperties"/>).
    /// Limits are applied BEFORE the <see cref="GeodeCache.SerializationRegistry"/>
    /// Lazy materialises so the per-converter snapshot picks them up.
    /// </summary>
    public static GeodeCache CreateCache(
        IServiceProvider sp,
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        // scope-per-cache: GeodeCache and SystemProperties are Scoped,
        // so we need a fresh scope to resolve them. The scope leaks for
        // the test's lifetime — acceptable in unit tests.
        var scope = sp.CreateScope();
        var sysProps = scope.ServiceProvider.GetRequiredService<SystemProperties>();
        sysProps.MaxDepth = maxDepth;
        sysProps.MaxArrayLength = maxArrayLength;
        sysProps.MaxBytesLength = maxBytesLength;
        sysProps.MaxStringLength = maxStringLength;
        return scope.ServiceProvider.GetRequiredService<GeodeCache>();
    }

    /// <summary>Shorthand: build SP + cache + return the registry.</summary>
    public static SerializationRegistry CreateRegistry(
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        var sp = BuildSp();
        // Build cache through a scope, then pull the registry from the
        // SAME scope so it sees the same SystemProperties values.
        var scope = sp.CreateScope();
        var sysProps = scope.ServiceProvider.GetRequiredService<SystemProperties>();
        sysProps.MaxDepth = maxDepth;
        sysProps.MaxArrayLength = maxArrayLength;
        sysProps.MaxBytesLength = maxBytesLength;
        sysProps.MaxStringLength = maxStringLength;
        var registry = scope.ServiceProvider.GetRequiredService<SerializationRegistry>();
        // RegisterBuiltInConverters moved to InitAsync (breaks the
        // ListDataConverter→SerializationRegistry circular dep at ctor
        // time). Tests must invoke it explicitly.
        registry.InitAsync().GetAwaiter().GetResult();
        return registry;
    }

    /// <summary>
    /// Encode <paramref name="value"/> through the registry and return
    /// the full wire bytes (DSCode + payload).
    /// </summary>
    /// <remarks>
    /// Test-only sync facade — blocks on the async write. Production
    /// callers all go through <see cref="SerializationRegistry.WriteObjectAsync"/>;
    /// the built-in converters do CPU work and return
    /// <see cref="ValueTask.CompletedTask"/>, so no deadlock risk.
    /// </remarks>
    public static byte[] Encode(object? value,
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        var registry = CreateRegistry(maxDepth, maxArrayLength, maxBytesLength, maxStringLength);
        using var writer = new DataOutput();
        registry.WriteObjectAsync(writer, value).AsTask().GetAwaiter().GetResult();
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Decode wire bytes through the registry. Bytes must start with a
    /// DSCode the registry can dispatch on.
    /// </summary>
    public static object? Decode(byte[] bytes,
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        var registry = CreateRegistry(maxDepth, maxArrayLength, maxBytesLength, maxStringLength);
        var reader = new DataInput(bytes);
        return registry.ReadObject(reader);
    }

    /// <summary>Encode then decode, casting back to <typeparamref name="T"/>.</summary>
    public static T RoundTrip<T>(T value,
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000) =>
        (T)Decode(Encode(value!, maxDepth, maxArrayLength, maxBytesLength, maxStringLength),
                  maxDepth, maxArrayLength, maxBytesLength, maxStringLength)!;
}
