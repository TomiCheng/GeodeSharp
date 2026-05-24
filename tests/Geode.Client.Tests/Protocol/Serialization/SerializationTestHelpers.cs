using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        var cache = ActivatorUtilities.CreateInstance<GeodeCache>(sp, "test-cache");
        cache.CacheProperties.MaxDepth = maxDepth;
        cache.CacheProperties.MaxArrayLength = maxArrayLength;
        cache.CacheProperties.MaxBytesLength = maxBytesLength;
        cache.CacheProperties.MaxStringLength = maxStringLength;
        return cache;
    }

    /// <summary>Shorthand: build SP + cache + return the registry.</summary>
    public static SerializationRegistry CreateRegistry(
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        var sp = BuildSp();
        var cache = CreateCache(sp, maxDepth, maxArrayLength, maxBytesLength, maxStringLength);
        return cache.SerializationRegistry;
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
