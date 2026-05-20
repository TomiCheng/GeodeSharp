using System;
using Geode.Client.Internal;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

internal sealed class SerializationRegistry
{
    private readonly Dictionary<byte, IDataConverter> _byDsCode = [];
    private readonly Dictionary<Type, IDataConverter> _byType = [];
    private readonly ObjectFactory<PdxLocalWriter> _pdxLocalWriterFactory;
    private readonly ObjectFactory<PdxWriterWithTypeCollector> _pdxWriterWithTypeCollectorFactory;
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
        _pdxLocalWriterFactory = ActivatorUtilities.CreateFactory<PdxLocalWriter>([]);
        _pdxWriterWithTypeCollectorFactory = ActivatorUtilities.CreateFactory<PdxWriterWithTypeCollector>([typeof(string)]);

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


    private bool TryWriteBuiltIn(DataOutput writer, object value, Type type, int depth)
    {
        if (!_byType.TryGetValue(type, out var converter) && type.IsGenericType)
        {
            _byType.TryGetValue(type.GetGenericTypeDefinition(), out converter);
        }

        if (converter is null) return false;

        var dsCode = converter.GetDsCode(value);
        writer.WriteByte(dsCode);
        converter.Write(writer, value, dsCode, depth);
        return true;
    }

    private bool TryWritePdx(DataOutput writer, object value, Type type)
    {
        if (!_typeRegistry.TryGetEntry(type, out var entry)) return false;
        var localPdxType = _pdxTypeRegistry.GetLocalPdxType(entry.ClassName);
        if (localPdxType is null)
        {
            // Step A:className 在本地 registry「沒看過」(第一次序列化)
            // A.1 ✓ — new PdxWriterWithTypeCollector(output, className, registry)
            using var ptc = _pdxWriterWithTypeCollectorFactory(_serviceProvider, [entry.ClassName]);
            // A.2 ✓ — entry.Write(value, ptc):跑 user ToData,base PdxLocalWriter
            //        的 WriteXxx 會把 field 順手蒐進 _fields(對應 cppcache
            //        WithTypeCollector::writeXxx 裡的 m_pdxType->addXxxField)。
            entry.Write(value, ptc);
            // A.3 ✓ — 把採集到的 schema 取出來,叫它算 field 對照表
            var nType = ptc.GetPdxLocalType();
            nType.Initialize();
            //   4. nTypeId = registry.GetPdxIdForType(className, pool, nType, true)
            //      — 同步 wire op,跟 server 拿 / 配 typeId
            //   5. nType.SetTypeId(nTypeId)
            //   6. ptc.EndObjectWriting() — 補 typeId / 計長度
            //   7. registry.AddLocalPdxType(className, nType)
            //              .AddPdxType(nTypeId, nType)
        }
        else
        {
            // Step B:本地已有 localPdxType(第二次以後)
            //   cppcache 註解:「now always remotewriter as we have API
            //   Read/WriteUnreadFields」— 不管物件身上有沒有 preserved data,
            //   都走 PdxRemoteWriter。
            //   1. preservedData = registry.GetPreserveData(value)
            //   2. if preservedData != null:
            //        mergedPdxType = registry.GetPdxType(preservedData.MergedTypeId)
            //        prw = new PdxRemoteWriter(output, mergedPdxType, preservedData, registry)
            //      else:
            //        prw = new PdxRemoteWriter(output, className, registry)
            //   3. entry.Write(value, prw)
            //   4. prw.EndObjectWriting()
        }

        // 目前暫行作法(Phase 2.1 walking skeleton):一路只用 PdxLocalWriter,
        // schema 直接呼叫 Build() 收回來丟 PdxTypeRegistry.ResolveTypeId。
        // 等 Step A / Step B 前置缺口補齊之後,上面 if/else 才會接管。
        //using var localWriter = _pdxLocalWriterFactory(_serviceProvider, []);
        //entry.Write(value, localWriter);
        //var (schema, payload) = localWriter.Build(entry.ClassName);
        //var typeId = _pdxTypeRegistry.ResolveTypeId(schema);

        //writer.WriteByte(DSCode.PDX);
        //writer.WriteInt32(payload.Length + sizeof(int));
        //writer.WriteInt32(typeId);
        //writer.WriteBytesOnly(payload);
        return true;
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

    private async ValueTask<bool> TryWritePdxAsync(DataOutput writer, object value, Type type, CancellationToken ct)
    {
        if (!_typeRegistry.TryGetEntry(type, out var entry)) return false;

        var localPdxType = _pdxTypeRegistry.GetLocalPdxType(entry.ClassName);
        if (localPdxType is null)
        {
            // Step A:className 在本地 registry「沒看過」(第一次序列化)
            // A.1 ✓ — new PdxWriterWithTypeCollector(output, className, registry)
            using var ptc = _pdxWriterWithTypeCollectorFactory(_serviceProvider, [entry.ClassName]);
            // A.2 ✓ — entry.Write(value, ptc):跑 user ToData,base PdxLocalWriter
            //        的 WriteXxx 會把 field 順手蒐進 _fields。
            entry.Write(value, ptc);
            // A.3 ✓ — 把採集到的 schema 取出來,叫它算 field 對照表
            var nType = ptc.GetPdxLocalType();
            nType.Initialize();
            // A.4 ✓ — 跟 server 拿 / 配 typeId(對齊 cppcache
            //        PdxTypeRegistry::getPDXIdForType,內部會打
            //        GET_PDX_ID_FOR_TYPE wire op 並把結果 cache 起來)。
            //        pool 從 DataOutput 帶下來(對齊 cppcache
            //        DataOutputInternal::getPool(output))。
            var nTypeId = await _pdxTypeRegistry.GetPdxIdForTypeAsync(
                className: entry.ClassName,
                pool: writer.Pool,
                nType: nType,
                checkIfThere: true,
                ct: ct);
            // A.5 ✓ — typeId 寫回 schema(對齊 cppcache nType->setTypeId(typeId);
            //        PdxType.TypeId 是 { get; set; },等效 setter)。
            nType.TypeId = nTypeId;
            // A.6 ✓ — 把採集到的 field-data payload(field bytes + offset
            //        table)收回來,加上 PDX wire header(DSCode + length +
            //        typeId)寫到外層 DataOutput。
            //        對齊 cppcache PdxWriterWithTypeCollector::endObjectWriting
            //        → PdxLocalWriter::writePdxHeader,但我們把 header
            //        framing 放在外層而非 writer 內部 buffer。
            var payload = ptc.BuildPayload();
            writer.WriteByte(DSCode.PDX);
            writer.WriteInt32(payload.Length + sizeof(int));   // length 含 typeId 那 4 bytes
            writer.WriteInt32(nTypeId);
            writer.WriteBytesOnly(payload);
            // A.7 ✓ — 把這個 schema 灌進兩個 cache(下一次同 className 走到
            //        TryWritePdxAsync 就會 GetLocalPdxType 命中,改走 Step B
            //        的 PdxRemoteWriter,不再打 wire op)。對齊 cppcache
            //        registry.addLocalPdxType / registry.addPdxType。
            _pdxTypeRegistry.AddLocalPdxType(entry.ClassName, nType);
            _pdxTypeRegistry.AddPdxType(nTypeId, nType);
        }
        else
        {
            // Step B:本地已有 localPdxType — 走 PdxRemoteWriter,尚未實作。
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

    public void WriteObject(DataOutput writer, object? value, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (depth >= MaxDepth)
        {
            throw new InvalidOperationException(
                $"SerializationRegistry: write exceeded MaxDepth ({MaxDepth}). "
                + "Refusing to serialise a potentially cyclic or pathologically "
                + "nested object graph. Tune GeodeClientOptions.Serialization.MaxDepth "
                + "if a legitimate workload needs deeper nesting.");
        }

        if (value is null)
        { 
            writer.WriteByte(DSCode.NullObj);
            return;
        }

        var type = value.GetType();
        if (TryWriteBuiltIn(writer, value, type, depth)) return;
        if (TryWritePdx(writer, value, type)) return;

        throw new NotSupportedException($"No SerializationRegistry converter registered for runtime type {type}.");
    }
}
