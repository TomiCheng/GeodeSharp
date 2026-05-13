using System.Buffers;
using Geode.Client.Internal;
using Geode.Client.Options;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Tests.Protocol.Serialization;

/// <summary>
/// Wire-level helpers for converter tests. Every assertion goes
/// through a freshly-constructed <see cref="SerializationRegistry"/>
/// so the test simultaneously validates the converter's
/// <c>Write</c>/<c>Read</c> bodies, the registry's
/// <c>WriteObject</c>/<c>ReadObject</c> dispatch (including the
/// DSCode byte the registry writes / reads), and the
/// <c>_byType</c>/<c>_byDsCode</c> registration.
/// </summary>
internal static class SerializationTestHelpers
{
    /// <summary>
    /// Spin up a fresh <see cref="SerializationRegistry"/> wired to a
    /// freshly-initialised <see cref="CacheScopeContext"/> and a
    /// minimal <see cref="IServiceProvider"/> that the registry uses
    /// to <see cref="ActivatorUtilities.CreateInstance{T}(IServiceProvider, object[])"/>
    /// its length-prefixed converters. Production resolves both via
    /// DI; tests build them directly so each case gets a clean,
    /// isolated registry without bootstrapping the whole container.
    /// </summary>
    /// <param name="maxDepth">
    /// Override for <see cref="SerializationOptions.MaxDepth"/>.
    /// Default matches production (<c>64</c>); depth-enforcement tests
    /// pass small values like <c>2</c> / <c>3</c> so the limit fires
    /// on a realistically small nested payload.
    /// </param>
    /// <param name="maxArrayLength">
    /// Override for <see cref="SerializationOptions.MaxArrayLength"/>.
    /// Default matches production (<c>1_000_000</c>); array-limit
    /// tests pass small values to exercise the check without building
    /// gigabyte payloads.
    /// </param>
    /// <param name="maxBytesLength">
    /// Override for <see cref="SerializationOptions.MaxBytesLength"/>.
    /// Default matches production (<c>10_000_000</c>); covers
    /// <c>byte[]</c> only.
    /// </param>
    /// <param name="maxStringLength">
    /// Override for <see cref="SerializationOptions.MaxStringLength"/>.
    /// Same default + same testing rationale as
    /// <paramref name="maxArrayLength"/>.
    /// </param>
    public static SerializationRegistry CreateRegistry(
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

        // Minimum DI container: just the CacheScopeContext we just
        // initialised, so ActivatorUtilities-constructed converters
        // inside the registry resolve the same scoped instance the
        // registry itself sees.
        var sp = new ServiceCollection()
            .AddSingleton(scope)
            .BuildServiceProvider();

        return new SerializationRegistry(sp, scope);
    }

    /// <summary>
    /// Encode <paramref name="value"/> through the registry and
    /// return the full wire bytes (DSCode byte + payload).
    /// </summary>
    public static byte[] Encode(object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new BigEndianBinaryWriter(buffer);
        CreateRegistry().WriteObject(writer, value);
        return buffer.WrittenSpan.ToArray();
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
