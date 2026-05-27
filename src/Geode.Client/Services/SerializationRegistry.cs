using System;
using Geode.Client.Protocol;
using Geode.Client.Protocol.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geode.Client.Services;

internal sealed class SerializationRegistry(
    IServiceProvider serviceProvider,
    SystemProperties systemProperties,
    TypeRegistry typeRegistry,
    PdxTypeRegistry pdxTypeRegistry,
    ILogger<SerializationRegistry> logger)
{
    private readonly Dictionary<byte, IDataConverter> _byDsCode = [];
    private readonly Dictionary<Type, IDataConverter> _byType = [];

    public Task InitAsync(CancellationToken ct = default)
    {
        RegisterBuiltInConverters();
        return Task.CompletedTask;
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
        // Scalars: no length-prefix on wire — no allocation DoS surface
        // — no GeodeCache injection needed. Plain `new ...()` keeps
        // these construction sites cheap.
        Register(new BooleanDataConverter());      // 53  CacheableBoolean   ??bool
        Register(new CharacterDataConverter());    // 54  CacheableCharacter ??char
        Register(new ByteDataConverter());         // 55  CacheableByte      ??byte (unsigned, .NET convention)
        Register(new Int16DataConverter());        // 56  CacheableInt16     ??short
        Register(new Int32DataConverter());        // 57  CacheableInt32     ??int
        Register(new Int64DataConverter());        // 58  CacheableInt64     ??long
        Register(new SingleDataConverter());       // 59  CacheableFloat     ??float
        Register(new DoubleDataConverter());       // 60  CacheableDouble    ??double
        Register(new DateTimeDataConverter());     // 61  CacheableDate      ??DateTime

        // Length-prefixed converters: snapshot
        // GeodeCache.CacheProperties.MaxArrayLength / MaxStringLength at
        // construction. Pass `_cache` explicitly to ActivatorUtilities —
        // GeodeCache isn't DI-registered, only the IServiceProvider is.
        Register(ActivatorUtilities.CreateInstance<BytesDataConverter>(serviceProvider));   // 46  CacheableBytes      → byte[]
        Register(ActivatorUtilities.CreateInstance<StringDataConverter>(serviceProvider));  // 42/87/88/89 (+69 read-only) → string

        Register(ActivatorUtilities.CreateInstance<BooleanArrayDataConverter>(serviceProvider)); // 26  BooleanArray         → bool[]
        Register(ActivatorUtilities.CreateInstance<CharArrayDataConverter>(serviceProvider));    // 27  CharArray            → char[]
        Register(ActivatorUtilities.CreateInstance<Int16ArrayDataConverter>(serviceProvider));   // 47  CacheableInt16Array  → short[]
        Register(ActivatorUtilities.CreateInstance<Int32ArrayDataConverter>(serviceProvider));   // 48  CacheableInt32Array  → int[]
        Register(ActivatorUtilities.CreateInstance<Int64ArrayDataConverter>(serviceProvider));   // 49  CacheableInt64Array  → long[]
        Register(ActivatorUtilities.CreateInstance<SingleArrayDataConverter>(serviceProvider));  // 50  CacheableFloatArray  → float[]
        Register(ActivatorUtilities.CreateInstance<DoubleArrayDataConverter>(serviceProvider));  // 51  CacheableDoubleArray → double[]
        // string[] and object[] both take a registry reference so
        // each element can re-enter WriteObject / ReadObject with
        // its own DSCode. Safe `this` pass ??converter stores the
        // reference but doesn't invoke anything on us until Write /
        // Read fires post-construction.
        Register(ActivatorUtilities.CreateInstance<StringArrayDataConverter>(serviceProvider)); // 64  CacheableStringArray ??string[]
        Register(ActivatorUtilities.CreateInstance<ObjectArrayDataConverter>(serviceProvider)); // 52  CacheableObjectArray ??object[]

        // Tier B-2 collections ??open-generic. Each ManagedType is
        // typeof(List<>) / typeof(HashSet<>) / typeof(Dictionary<,>);
        // WriteObject's dispatch falls back to
        // GetGenericTypeDefinition() so one converter instance handles
        // every closed instantiation. Target-shape conversion
        // (List<object?> ??IList<int>, HashSet<object?> ??ISet<int>,
        // Dictionary<object,object?> ??Dictionary<K,V>, ?? happens
        // post-decode at TypedResultAdapter, not here.
        Register(ActivatorUtilities.CreateInstance<LinkedListDataConverter>(serviceProvider));  // 10  CacheableLinkedList ??LinkedList<T>
        Register(ActivatorUtilities.CreateInstance<ListDataConverter>(serviceProvider));        // 65  CacheableArrayList  ??List<T>
        Register(ActivatorUtilities.CreateInstance<HashSetDataConverter>(serviceProvider));     // 66  CacheableHashSet    ??HashSet<T>
        Register(ActivatorUtilities.CreateInstance<DictionaryDataConverter>(serviceProvider));  // 67  CacheableHashMap    ??Dictionary<K,V>
        Register(ActivatorUtilities.CreateInstance<StackDataConverter>(serviceProvider));       // 74  CacheableStack      ??Stack<T>
    }


    //internal int MaxArrayLength => _cache.CacheProperties.MaxArrayLength;

    //internal int MaxDepth => _cache.CacheProperties.MaxDepth;

    //internal int MaxStringLength => _cache.CacheProperties.MaxStringLength;

    public bool IsRegistered(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byType.ContainsKey(type)) return true;
        if (type.IsGenericType && _byType.ContainsKey(type.GetGenericTypeDefinition())) return true;
        return false;
    }

    //public async ValueTask WriteObjectAsync(DataOutput writer, object? value, int depth = 0,
    //    CancellationToken ct = default)
    //{
    //    ArgumentNullException.ThrowIfNull(writer);

    //    if (depth >= MaxDepth)
    //    {
    //        throw new InvalidOperationException(
    //            $"SerializationRegistry: write exceeded MaxDepth ({MaxDepth}).");
    //    }

    //    if (value is null)
    //    {
    //        writer.WriteByte(DSCode.NullObj);
    //        return;
    //    }

    //    var type = value.GetType();
    //    if (await TryWriteBuiltInAsync(writer, value, type, depth, ct)) return;
    //    //if (await TryWritePdxAsync(writer, value, type, ct)) return;

    //    throw new NotSupportedException($"No SerializationRegistry converter registered for runtime type {type}.");
    //}

    public async ValueTask WriteObjectAsync(DataOutput writer, object? value, int depth = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (depth >= systemProperties.MaxDepth)
        {
            throw new InvalidOperationException($"SerializationRegistry: write exceeded MaxDepth ({systemProperties.MaxDepth}).");
        }

        if (value is null)
        {
            writer.WriteByte(DSCode.NullObj);
            return;
        }

        var type = value.GetType();
        if (await TryWriteBuiltInAsync(writer, value, type, depth, ct)) return;
        if (await TryWritePdxAsync(writer, value, type, depth, ct)) return;

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

    private async ValueTask<bool> TryWritePdxAsync(DataOutput writer, object value, Type type, int _, CancellationToken ct)
    {
        if (!typeRegistry.TryGetEntry(type, out var entry)) return false;

        var localPdxType = pdxTypeRegistry.GetLocalPdxType(entry.ClassName);
        if (localPdxType is null)
        {
            using var ptc = PdxWriterWithTypeCollector.Create(serviceProvider, entry.ClassName);
            entry.Write(value, ptc);
            var nType = ptc.GetPdxLocalType();
            nType.Initialize();
            nType.TypeId = await pdxTypeRegistry.GetPdxIdForTypeAsync(entry.ClassName, nType, true, ct);

            var payload = ptc.BuildPayload();
            writer.WriteByte(DSCode.PDX);
            writer.WriteInt32(payload.Length + sizeof(int));
            writer.WriteInt32(nType.TypeId);
            writer.WriteBytesOnly(payload);

            pdxTypeRegistry.AddLocalPdxType(entry.ClassName, nType);
            pdxTypeRegistry.AddPdxType(nType.TypeId, nType);
        }
        else
        {
            // Step B — schema cached. cppcache always picks PdxRemoteWriter
            // here because WriteUnreadFields is part of the public API; we
            // can't tell upfront whether the user will use it.

            // B.1 Look up unread-field bytes preserved from a prior
            //     deserialize. null is the common case.
            var preservedData = pdxTypeRegistry.GetPreserveData(value);

            // B.2 Two PdxRemoteWriter ctor forms (cppcache PdxHelper.cpp:128).
            PdxRemoteWriter prw;
            if (preservedData is not null)
            {
                var mergedPdxType = pdxTypeRegistry.GetPdxType(preservedData.MergedTypeId)
                    ?? throw new GeodeException(
                        $"PdxTypeRegistry: merged typeId {preservedData.MergedTypeId} " +
                        $"referenced by preserved data is not in the by-typeId cache.");
                prw = PdxRemoteWriter.Create(serviceProvider, mergedPdxType, preservedData);
            }
            else
            {
                prw = PdxRemoteWriter.Create(serviceProvider, entry.ClassName);
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

    public object? ReadObject(DataInput reader, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (depth >= systemProperties.MaxDepth)
        {
            throw new GeodeException(
                $"SerializationRegistry: read exceeded MaxDepth ({systemProperties.MaxDepth}). "
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
    public ValueTask<object?> ReadObjectAsync(DataInput reader, int depth = 0, CancellationToken ct = default)
    {
        if (depth >= systemProperties.MaxDepth)
        {
            throw new GeodeException(
                $"SerializationRegistry: read exceeded MaxDepth ({systemProperties.MaxDepth}). "
                + "The server payload is more deeply nested than the client "
                + "permits ??treat as hostile or buggy unless a legitimate "
                + "workload warrants it, in which case tune "
                + "GeodeClientOptions.Serialization.MaxDepth.");
        }

        var dsCode = reader.ReadByte();

        if (dsCode == DSCode.NullObj)
        {
            return ValueTask.FromResult<object?>(null);
        }

        if (dsCode == DSCode.PDX)
        {
            return ReadPdxAsync(reader, depth, ct);
        }
        if (_byDsCode.TryGetValue(dsCode, out var converter))
        {
            return ValueTask.FromResult<object?>(converter.Read(reader, dsCode, depth));
        }

        throw new GeodeException($"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }

    /// <summary>
    /// Decode a PDX (DSCode 93) wire frame. Mirror of cppcache
    /// <c>PdxHelper::deserializePdx(DataInput&amp;)</c>
    /// (<c>cppcache/src/PdxHelper.cpp:287</c>) — the outer overload that
    /// reads <c>length</c> + <c>typeId</c> off the wire, then delegates to
    /// the inner <c>deserializePdx(input, typeId, length)</c>
    /// (<c>PdxHelper.cpp:153</c>) for the actual field decode.
    /// </summary>
    private async ValueTask<object?> ReadPdxAsync(DataInput reader, int depth, CancellationToken ct)
    {
        // Entry breadcrumb — cppcache PdxHelper.cpp doesn't log on entry of
        // the outer deserializePdx, but a wire-bug repro often needs the
        // ".NET saw a PDX frame" event paired against cppcache's hex dump.
        logger.LogDebug("ReadPdxAsync: entering, depth={Depth}", depth);

        // R.1 Read pdxLength (4 BE) — covers typeId + payload bytes.
        var pdxLength = reader.ReadInt32();

        // R.2 Read typeId (4 BE).
        var typeId = reader.ReadInt32();

        // R.3 PdxType lookup. Local cache first (cppcache PdxHelper.cpp:163
        //     getPdxType); on miss, GET_PDX_TYPE_BY_ID via dm
        //     (cppcache PdxHelper.cpp:203-207). After fetch, cache both
        //     ways so subsequent reads don't re-hit the wire (cppcache
        //     PdxHelper.cpp:57-59 in checkAndFetchPdxType — same pattern).
        var pdxType = pdxTypeRegistry.GetPdxType(typeId);
        if (pdxType is null)
        {
            pdxType = await pdxTypeRegistry.GetPdxTypeByIdAsync(typeId, ct);
            pdxTypeRegistry.AddPdxType(typeId, pdxType);
            pdxTypeRegistry.AddLocalPdxType(pdxType.ClassName, pdxType);
        }

        // R.4 Look up the user's IPdxSerializable<T> factory by className.
        //     cppcache: SerializationRegistry::getPdxSerializableType.
        //     Missing here means the caller never RegisterPdxType<T>'d this
        //     className — surface a useful error rather than fall through
        //     to a NullReference later.
        if (!typeRegistry.TryGetEntryByClassName(pdxType.ClassName, out var entry))
        {
            throw new GeodeException(
                $"PDX className '{pdxType.ClassName}' (typeId={typeId}) is not " +
                $"registered locally — call cache.TypeRegistry.RegisterPdxType<T>() " +
                $"for the matching .NET type first.");
        }

        // Mirror cppcache LOGDEBUG (PdxHelper.cpp:171) — paired with the
        // entry breadcrumb so a wire-bug trace can match cppcache's against
        // ours line-by-line.
        logger.LogDebug("deserializePdx ClassName = {ClassName}, isLocal = {IsLocal}",
            pdxType.ClassName, pdxType.IsLocal);

        // R.5 Pick reader: PdxLocalReader (schema is local, no unread
        //     fields) vs PdxRemoteReader (remote has fields we don't know;
        //     captures unread bytes for later SetPreserveData). cppcache
        //     PdxType::isLocal() drives the choice (PdxHelper.cpp:175).
        PdxLocalReader pdxReader = pdxType.IsLocal
            ? PdxLocalReader.Create(serviceProvider, pdxType, reader, pdxLength)
            : PdxRemoteReader.Create(serviceProvider, pdxType, reader, pdxLength);

        // R.6 Run user's FromData(IPdxReader) — constructs the .NET object
        //     by calling ReadXxx(name) on the reader for each field.
        //     cppcache PdxHelper.cpp:178 / :182.
        //     TODO: cppcache calls plr.moveStream() after FromData to
        //     advance the buffer cursor past the PDX frame. Not needed
        //     yet — our DecodeValuePart constructs a fresh DataInput per
        //     part, so the cursor is discarded with the buffer. Add when
        //     a container converter reads a PDX field mid-stream.
        var value = entry.Read(pdxReader);

        // R.7 If PdxRemoteReader and it captured preserved bytes:
        //     _pdxTypeRegistry.SetPreserveData(value, ...) — inverse of
        //     the current GetPreserveData null stub. Skipped until the
        //     remote-schema divergence scenario shows up.
        return value;
    }
}
