using System;
using Geode.Client.Internal;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

internal sealed class SerializationRegistry
{
    private readonly Dictionary<byte, IDataConverter> _byDsCode = [];
    private readonly Dictionary<Type, IDataConverter> _byType = [];
    private readonly ObjectFactory<PdxWriterWithTypeCollector> _pdxWriterWithTypeCollectorFactory;
    private readonly ObjectFactory<PdxRemoteWriter> _pdxRemoteWriterByClassNameFactory;
    private readonly ObjectFactory<PdxRemoteWriter> _pdxRemoteWriterByPdxTypeFactory;
    private readonly PdxTypeRegistry _pdxTypeRegistry;
    private readonly CacheScopeContext _scopeContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly TypeRegistry _typeRegistry;

    public SerializationRegistry(
        IServiceProvider serviceProvider,
        CacheScopeContext scopeContext,
        TypeRegistry typeRegistry,
        PdxTypeRegistry pdxTypeRegistry)
    {
        _serviceProvider = serviceProvider;
        _typeRegistry = typeRegistry;
        _pdxTypeRegistry = pdxTypeRegistry;
        _scopeContext = scopeContext;
        _pdxWriterWithTypeCollectorFactory = ActivatorUtilities.CreateFactory<PdxWriterWithTypeCollector>([typeof(string)]);
        _pdxRemoteWriterByClassNameFactory = ActivatorUtilities.CreateFactory<PdxRemoteWriter>([typeof(string)]);
        _pdxRemoteWriterByPdxTypeFactory = ActivatorUtilities.CreateFactory<PdxRemoteWriter>([typeof(PdxType), typeof(PdxRemotePreservedData)]);

        RegisterBuiltInConverters();
    }

    private void Register(IDataConverter converter)
    {
        foreach (var dsCode in converter.DsCodes)
        {
            _byDsCode[dsCode] = converter;
        }
        _byType[converter.ManagedType] = converter;
    }

    private void RegisterBuiltInConverters()
    {
        // Order: scalar (sorted by DSCode), then bytes, then string,
        // then arrays (sorted by DSCode).
        // Scalars: no length-prefix on wire ??no allocation DoS
        // surface ??no CacheScopeContext injection needed. Plain
        // `new ??)` keeps these construction sites cheap.
        Register(new BooleanDataConverter());      // 53  CacheableBoolean   ??bool
        Register(new CharacterDataConverter());    // 54  CacheableCharacter ??char
        Register(new ByteDataConverter());         // 55  CacheableByte      ??byte (unsigned, .NET convention)
        Register(new Int16DataConverter());        // 56  CacheableInt16     ??short
        Register(new Int32DataConverter());        // 57  CacheableInt32     ??int
        Register(new Int64DataConverter());        // 58  CacheableInt64     ??long
        Register(new SingleDataConverter());       // 59  CacheableFloat     ??float
        Register(new DoubleDataConverter());       // 60  CacheableDouble    ??double
        Register(new DateTimeDataConverter());     // 61  CacheableDate      ??DateTime

        // Length-prefixed converters: read CacheScopeContext via DI to
        // snapshot Serialization.MaxArrayLength / MaxStringLength at
        // construction. ActivatorUtilities resolves the scoped
        // CacheScopeContext from _serviceProvider ??same instance the
        // registry itself sees.
        Register(ActivatorUtilities.CreateInstance<BytesDataConverter>(_serviceProvider));        // 46  CacheableBytes     ??byte[]
        Register(ActivatorUtilities.CreateInstance<StringDataConverter>(_serviceProvider));                                                                // 42/87/88/89 (+69 read-only) ??string

        Register(ActivatorUtilities.CreateInstance<BooleanArrayDataConverter>(_serviceProvider)); // 26  BooleanArray       ??bool[]
        Register(ActivatorUtilities.CreateInstance<CharArrayDataConverter>(_serviceProvider));    // 27  CharArray          ??char[]
        Register(ActivatorUtilities.CreateInstance<Int16ArrayDataConverter>(_serviceProvider));   // 47  CacheableInt16Array ??short[]
        Register(ActivatorUtilities.CreateInstance<Int32ArrayDataConverter>(_serviceProvider));   // 48  CacheableInt32Array ??int[]
        Register(ActivatorUtilities.CreateInstance<Int64ArrayDataConverter>(_serviceProvider));   // 49  CacheableInt64Array ??long[]
        Register(ActivatorUtilities.CreateInstance<SingleArrayDataConverter>(_serviceProvider));  // 50  CacheableFloatArray ??float[]
        Register(ActivatorUtilities.CreateInstance<DoubleArrayDataConverter>(_serviceProvider));  // 51  CacheableDoubleArray ??double[]
        // string[] and object[] both take a registry reference so
        // each element can re-enter WriteObject / ReadObject with
        // its own DSCode. Safe `this` pass ??converter stores the
        // reference but doesn't invoke anything on us until Write /
        // Read fires post-construction.
        Register(new StringArrayDataConverter(this)); // 64  CacheableStringArray ??string[]
        Register(new ObjectArrayDataConverter(this)); // 52  CacheableObjectArray ??object[]

        // Tier B-2 collections ??open-generic. Each ManagedType is
        // typeof(List<>) / typeof(HashSet<>) / typeof(Dictionary<,>);
        // WriteObject's dispatch falls back to
        // GetGenericTypeDefinition() so one converter instance handles
        // every closed instantiation. Target-shape conversion
        // (List<object?> ??IList<int>, HashSet<object?> ??ISet<int>,
        // Dictionary<object,object?> ??Dictionary<K,V>, ?? happens
        // post-decode at TypedResultAdapter, not here.
        Register(new LinkedListDataConverter(this));  // 10  CacheableLinkedList ??LinkedList<T>
        Register(new ListDataConverter(this));        // 65  CacheableArrayList  ??List<T>
        Register(new HashSetDataConverter(this));     // 66  CacheableHashSet    ??HashSet<T>
        Register(new DictionaryDataConverter(this));  // 67  CacheableHashMap    ??Dictionary<K,V>
        Register(new StackDataConverter(this));       // 74  CacheableStack      ??Stack<T>
    }


    internal int MaxArrayLength => _scopeContext.Options.Serialization.MaxArrayLength;

    internal int MaxDepth => _scopeContext.Options.Serialization.MaxDepth;

    internal int MaxStringLength => _scopeContext.Options.Serialization.MaxStringLength;

    public bool IsRegistered(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byType.ContainsKey(type)) return true;
        if (type.IsGenericType && _byType.ContainsKey(type.GetGenericTypeDefinition())) return true;
        return false;
    }

    /// <summary>
    /// Async 版本的 <see cref="ReadObject"/>。Default 行為跟 sync 相同;
    /// converter 自己決定要不要真的 await(用 default interface method 的話
    /// 就是包 sync,override 的話可以真 async)。
    /// </summary>
    public async ValueTask<object?> ReadObjectAsync(BigEndianBinaryReader reader, int depth = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (depth >= MaxDepth)
        {
            throw new GeodeException(
                $"SerializationRegistry: read exceeded MaxDepth ({MaxDepth}).");
        }

        var dsCode = reader.ReadByte();
        if (dsCode == DSCode.NullObj) return null;
        if (_byDsCode.TryGetValue(dsCode, out var converter))
        {
            return await converter.ReadAsync(reader, dsCode, depth, ct);
        }
        throw new GeodeException($"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }

    /// <summary>
    /// Async 版本的 <see cref="WriteObject"/>。PDX 路徑(<see cref="TryWritePdxAsync"/>)
    /// 之後會在這條鏈裡 await wire op(A.4 GetPdxIdForType)。
    /// </summary>
    public async ValueTask WriteObjectAsync(DataOutput writer, object? value, int depth = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (depth >= MaxDepth)
        {
            throw new InvalidOperationException(
                $"SerializationRegistry: write exceeded MaxDepth ({MaxDepth}).");
        }

        if (value is null)
        {
            writer.WriteByte(DSCode.NullObj);
            return;
        }

        var type = value.GetType();
        if (await TryWriteBuiltInAsync(writer, value, type, depth, ct)) return;
        if (await TryWritePdxAsync(writer, value, type, ct)) return;

        throw new NotSupportedException($"No SerializationRegistry converter registered for runtime type {type}.");
    }

    private async ValueTask<bool> TryWriteBuiltInAsync(DataOutput writer, object value, Type type, int depth, CancellationToken ct)
    {
        if (!_byType.TryGetValue(type, out var converter) && type.IsGenericType)
        {
            _byType.TryGetValue(type.GetGenericTypeDefinition(), out converter);
        }
        if (converter is null) return false;

        var dsCode = converter.GetDsCode(value);
        writer.WriteByte(dsCode);
        await converter.WriteAsync(writer, value, dsCode, depth, ct);
        return true;
    }

    /// <summary>
    /// Encode <paramref name="value"/> as a PDX wire frame. Returns
    /// <see langword="false"/> when <paramref name="type"/> isn't registered
    /// as PDX (caller falls through to the unsupported-type throw).
    /// Mirror of cppcache <c>PdxHelper::serializePdx</c>
    /// (<c>PdxHelper.cpp:87</c>).
    /// </summary>
    /// <remarks>
    /// Two branches keyed on whether the className has been collected
    /// locally before:
    /// <list type="bullet">
    ///   <item><b>Step A</b> (first time): collect schema via
    ///         <see cref="PdxWriterWithTypeCollector"/>, round-trip to
    ///         server for typeId, cache both, emit frame.</item>
    ///   <item><b>Step B</b> (subsequent): reuse the cached schema/typeId
    ///         via <see cref="PdxRemoteWriter"/>; ctor form depends on
    ///         whether the value carries preserved unread fields.</item>
    /// </list>
    /// </remarks>
    private async ValueTask<bool> TryWritePdxAsync(DataOutput writer, object value, Type type, CancellationToken ct)
    {
        if (!_typeRegistry.TryGetEntry(type, out var entry)) return false;

        var localPdxType = _pdxTypeRegistry.GetLocalPdxType(entry.ClassName);
        if (localPdxType is null)
        {
            using var ptc = _pdxWriterWithTypeCollectorFactory(_serviceProvider, [entry.ClassName]);
            entry.Write(value, ptc);
            var nType = ptc.GetPdxLocalType();
            nType.Initialize();

            // A.4 Round-trip to the server to get a cluster-wide typeId.
            //     pool comes from the DataOutput (mirror cppcache
            //     DataOutputInternal::getPool).
            nType.TypeId = await _pdxTypeRegistry.GetPdxIdForTypeAsync(entry.ClassName, writer.Pool, nType, true, ct);

            // A.6 Emit the PDX wire frame: DSCode + length + typeId + payload.
            //     Length covers typeId + payload (cppcache PdxLocalWriter::
            //     writePdxHeader convention).
            var payload = ptc.BuildPayload();
            writer.WriteByte(DSCode.PDX);
            writer.WriteInt32(payload.Length + sizeof(int));
            writer.WriteInt32(nType.TypeId);
            writer.WriteBytesOnly(payload);

            // A.7 Cache the schema in both maps so the next call hits
            //     Step B (no wire op).
            _pdxTypeRegistry.AddLocalPdxType(entry.ClassName, nType);
            _pdxTypeRegistry.AddPdxType(nType.TypeId, nType);
        }
        else
        {
            // Step B — schema cached. cppcache always picks PdxRemoteWriter
            // here because WriteUnreadFields is part of the public API; we
            // can't tell upfront whether the user will use it.

            // B.1 Look up unread-field bytes preserved from a prior
            //     deserialize. null is the common case.
            var preservedData = _pdxTypeRegistry.GetPreserveData(value);

            // B.2 Two PdxRemoteWriter ctor forms (cppcache PdxHelper.cpp:128).
            PdxRemoteWriter prw;
            if (preservedData is not null)
            {
                var mergedPdxType = _pdxTypeRegistry.GetPdxType(preservedData.MergedTypeId)
                    ?? throw new GeodeException(
                        $"PdxTypeRegistry: merged typeId {preservedData.MergedTypeId} " +
                        $"referenced by preserved data is not in the by-typeId cache.");
                prw = _pdxRemoteWriterByPdxTypeFactory(_serviceProvider, [mergedPdxType, preservedData]);
            }
            else
            {
                prw = _pdxRemoteWriterByClassNameFactory(_serviceProvider, [entry.ClassName]);
            }

            using (prw)
            {
                // B.3 Run user ToData; PdxLocalWriter emits the field
                //     bytes against the existing schema.
                // TODO: cppcache PdxRemoteWriter overrides each WriteXxx
                //       to splice in preservedData's unread bytes — wire
                //       that up once WriteUnreadFields lands.
                entry.Write(value, prw);

                // B.4 Same frame layout as A.6; typeId picks the merged
                //     schema when preserved data is present, otherwise the
                //     local one.
                var schema = prw.MergedPdxType ?? localPdxType;
                var payload = prw.BuildPayload();
                writer.WriteByte(DSCode.PDX);
                writer.WriteInt32(payload.Length + sizeof(int));
                writer.WriteInt32(schema.TypeId);
                writer.WriteBytesOnly(payload);
            }
        }

        return true;
    }

    public object? ReadObject(BigEndianBinaryReader reader, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (depth >= MaxDepth)
        {
            throw new GeodeException(
                $"SerializationRegistry: read exceeded MaxDepth ({MaxDepth}). "
                + "The server payload is more deeply nested than the client "
                + "permits ??treat as hostile or buggy unless a legitimate "
                + "workload warrants it, in which case tune "
                + "GeodeClientOptions.Serialization.MaxDepth.");
        }

        var dsCode = reader.ReadByte();

        if (dsCode == DSCode.NullObj)
        {
            return null;
        }

        // TODO Phase 2+: PDX fall-through ??        //   if (dsCode == DSCode.PDX) return ReadPdx(reader);

        if (_byDsCode.TryGetValue(dsCode, out var converter))
        {
            return converter.Read(reader, dsCode, depth);
        }

        throw new GeodeException($"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }

}
