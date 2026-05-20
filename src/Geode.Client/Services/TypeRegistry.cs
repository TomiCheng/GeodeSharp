using System.Collections.Concurrent;
using Geode.Client.Pdx;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

/// <summary>
/// Default <see cref="ITypeRegistry"/>. Scoped (per-cache); mirror of cppcache
/// <c>TypeRegistry</c> (<c>cppcache/include/geode/TypeRegistry.hpp</c>).
/// </summary>
internal sealed class TypeRegistry(Cache cache, ILogger<TypeRegistry> logger) : ITypeRegistry
{
    private readonly Cache _cache = cache;
    private readonly ConcurrentDictionary<Type, PdxEntry> _byType = new();

    public void RegisterPdxType<T>(string? className = null) where T : IPdxSerializable<T>
    {
        // cppcache flow: TypeRegistry::registerPdxType → SerializationRegistry::
        //   addPdxSerializableType → TheTypeMap::bindPdxSerializable
        //   (SerializationRegistry.cpp:703-715). cppcache key is className
        //   from obj->getClassName(); we accept it as an optional parameter
        //   defaulting to typeof(T).FullName.
        var entry = new PdxEntry(
            ClrType: typeof(T),
            ClassName: className ?? typeof(T).FullName!,
            Write: (obj, w) => ((T)obj).ToData(w),
            Read: r => T.FromData(r)!);

        AddOrThrow(entry);
    }

    public void RegisterPdxSerializer<T>(IPdxSerializer<T> serializer, string? className = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // External path. cppcache uses one global PdxSerializer per cache
        // (SerializationRegistry::setPdxSerializer, className-switch
        // internally). We modernize to per-type IPdxSerializer<T> and
        // collapse into the same _byType dict so wire dispatch has one
        // lookup path regardless of intrusive/external origin.
        var entry = new PdxEntry(
            ClrType: typeof(T),
            ClassName: className ?? typeof(T).FullName!,
            Write: (obj, w) => serializer.ToData((T)obj, w),
            Read: r => serializer.FromData(r)!);

        AddOrThrow(entry);
    }

    private void AddOrThrow(PdxEntry entry)
    {
        if (_byType.TryAdd(entry.ClrType, entry)) return;

        // Mirror cppcache LOGERROR + IllegalStateException
        // (SerializationRegistry.cpp:709-713).
        logger.LogError("PDX type {ClrType} is already registered.", entry.ClrType.FullName);
        throw new InvalidOperationException(
            $"PDX type '{entry.ClrType.FullName}' is already registered.");
    }

    private readonly record struct PdxEntry(
        Type ClrType,
        string ClassName,
        Action<object, IPdxWriter> Write,
        Func<IPdxReader, object> Read);
}
