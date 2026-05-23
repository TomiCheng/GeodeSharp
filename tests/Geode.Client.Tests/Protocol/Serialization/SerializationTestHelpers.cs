/*
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Wire-level helpers for converter tests. Builds a minimal
/// <see cref="IServiceProvider"/> containing the scoped services
/// production uses (<see cref="CacheScopeContext"/>,
/// <see cref="TypeRegistry"/>, <see cref="PdxTypeRegistry"/>,
/// <see cref="SerializationRegistry"/>) so tests can resolve via
/// <see cref="ActivatorUtilities"/> the same way as production code.
/// </summary>
internal static class SerializationTestHelpers
{
    /// <summary>Build the test SP. Tests resolve services via this.</summary>
    public static IServiceProvider BuildSp(
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000)
    {
        var scope = new CacheScopeContext();
        var opts = new GeodeClientOptions();
        opts.Serialization.MaxDepth = maxDepth;
        opts.Serialization.MaxArrayLength = maxArrayLength;
        opts.Serialization.MaxBytesLength = maxBytesLength;
        opts.Serialization.MaxStringLength = maxStringLength;
        scope.Initialize(string.Empty, opts);

        return new ServiceCollection()
            .AddSingleton(scope)
            .AddSingleton<TypeRegistry>(_ => new TypeRegistry(NullLogger<TypeRegistry>.Instance))
            .AddSingleton<PdxTypeRegistry>()
            .AddSingleton<SerializationRegistry>()
            .BuildServiceProvider();
    }

    /// <summary>Shorthand: resolve a fresh <see cref="SerializationRegistry"/>.</summary>
    public static SerializationRegistry CreateRegistry(
        int maxDepth = 64,
        int maxArrayLength = 1_000_000,
        int maxBytesLength = 10_000_000,
        int maxStringLength = 1_000_000) =>
        BuildSp(maxDepth, maxArrayLength, maxBytesLength, maxStringLength)
            .GetRequiredService<SerializationRegistry>();

    /// <summary>
    /// Encode <paramref name="value"/> through the registry and
    /// return the full wire bytes (DSCode byte + payload).
    /// </summary>
    public static byte[] Encode(object value)
    {
        // Test-only sync facade. Production callers all go through
        // WriteObjectAsync; this helper blocks because xUnit assertion
        // sites are convenient when sync. The async path never actually
        // awaits anything for non-PDX values (built-in converters do CPU
        // work and return CompletedTask), so no deadlock risk here.
        var sp = BuildSp();
        using var writer = ActivatorUtilities.CreateInstance<DataOutput>(sp);
        sp.GetRequiredService<SerializationRegistry>()
            .WriteObjectAsync(writer, value).AsTask().GetAwaiter().GetResult();
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Decode wire bytes through the registry. Bytes must start with
    /// a DSCode byte the registry can dispatch on.
    /// </summary>
    public static object? Decode(byte[] bytes)
    {
        var reader = new BigEndianBinaryReader(bytes);
        return CreateRegistry().ReadObject(reader);
    }

    /// <summary>
    /// Encode then decode, asserting the value survives the wire.
    /// Caller picks the expected CLR result type via
    /// <typeparamref name="T"/>.
    /// </summary>
    public static T RoundTrip<T>(T value) =>
        (T)Decode(Encode(value!))!;
}

*/