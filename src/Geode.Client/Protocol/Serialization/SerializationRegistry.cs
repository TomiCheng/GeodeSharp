using System;
using Geode.Client.Internal;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Per-cache codec registry. Mirrors cppcache
/// <c>SerializationRegistry</c>
/// (<c>cppcache/src/SerializationRegistry.hpp/.cpp</c>) — owns the
/// DSCode ↔ <see cref="IDataConverter"/> mapping and provides the
/// central <see cref="WriteObject"/> / <see cref="ReadObject"/>
/// dispatch every wire op routes through for key / value
/// serialisation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two storage indices for one converter set.</b> Each registered
/// <see cref="IDataConverter"/> goes into both
/// <see cref="_byDsCode"/> (decode key = wire byte) and
/// <see cref="_byType"/> (encode key = runtime CLR type). The two
/// dicts are intentionally not merged into one — decode and encode
/// dispatch by different keys.
/// </para>
/// <para>
/// <b>Multi-DSCode converters.</b> A single converter can register
/// against multiple DSCodes (one CLR type, many wire forms — see
/// <c>StringDataConverter</c>). <see cref="Register"/> iterates
/// <see cref="IDataConverter.DsCodes"/> and points each entry at the
/// same instance.
/// </para>
/// <para>
/// <b>Per-cache scope.</b> Registered as DI Scoped alongside
/// <see cref="Services.Cache"/> so multi-cluster setups can have
/// different custom-type registrations per cluster without leaking
/// across.
/// </para>
/// <para>
/// <b>DSCode byte ownership.</b> Registry writes / reads the DSCode
/// byte on both sides of the wire and passes it back to the
/// converter so multi-DSCode converters can branch. Mirrors cppcache
/// <c>DataOutput::writeObject</c> calling
/// <c>ptr-&gt;getDsCode()</c> then writing the byte then calling
/// <c>ptr-&gt;toData(*this)</c>.
/// </para>
/// <para>
/// <b>PDX path is a Phase 2+ TODO.</b> The
/// <see cref="ReadObject"/> dispatch reserves <c>DSCode.PDX</c> for
/// the PDX branch; built-in converter registration covers everything
/// MVP needs.
/// </para>
/// </remarks>
internal sealed class SerializationRegistry
{

    private readonly Dictionary<byte, IDataConverter> _byDsCode = [];
    private readonly Dictionary<Type, IDataConverter> _byType = [];
    private readonly IServiceProvider _serviceProvider;

    private readonly TypeRegistry _typeRegistry;
    private readonly PdxTypeRegistry _pdxTypeRegistry;

    // Cached during RegisterBuiltInConverters; PdxLocalWriter takes it
    // via ctor to encode PDX string fields via the same code path as
    // top-level CacheableString.
    private StringDataConverter _stringConverter = null!;

    public SerializationRegistry(
        IServiceProvider serviceProvider,
        CacheScopeContext scopeContext,
        TypeRegistry typeRegistry,
        PdxTypeRegistry pdxTypeRegistry)
    {
        _serviceProvider = serviceProvider;
        _typeRegistry = typeRegistry;
        _pdxTypeRegistry = pdxTypeRegistry;
        ArgumentNullException.ThrowIfNull(scopeContext);
        MaxDepth = scopeContext.Options.Serialization.MaxDepth;
        MaxArrayLength = scopeContext.Options.Serialization.MaxArrayLength;
        MaxStringLength = scopeContext.Options.Serialization.MaxStringLength;

        RegisterBuiltInConverters();
    }

    /// <summary>
    /// Register the full built-in converter set (Tier A scalars + bytes /
    /// string, primitive arrays, Tier B-2 generic collections). cppcache
    /// registers ~30 of these at <c>SerializationRegistry</c> construction;
    /// we add them as their wire formats land. Phase 1.2 shipped int32 +
    /// boolean (the walking-skeleton minimum); Phase 1.3.0 widened to
    /// Tier A; Phase 1.3.d added the primitive-array tier.
    /// </summary>
    private void RegisterBuiltInConverters()
    {
        // Order: scalar (sorted by DSCode), then bytes, then string,
        // then arrays (sorted by DSCode).
        // Scalars: no length-prefix on wire → no allocation DoS
        // surface → no CacheScopeContext injection needed. Plain
        // `new …()` keeps these construction sites cheap.
        Register(new BooleanDataConverter());      // 53  CacheableBoolean   → bool
        Register(new CharacterDataConverter());    // 54  CacheableCharacter → char
        Register(new ByteDataConverter());         // 55  CacheableByte      → byte (unsigned, .NET convention)
        Register(new Int16DataConverter());        // 56  CacheableInt16     → short
        Register(new Int32DataConverter());        // 57  CacheableInt32     → int
        Register(new Int64DataConverter());        // 58  CacheableInt64     → long
        Register(new SingleDataConverter());       // 59  CacheableFloat     → float
        Register(new DoubleDataConverter());       // 60  CacheableDouble    → double
        Register(new DateTimeDataConverter());     // 61  CacheableDate      → DateTime

        // Length-prefixed converters: read CacheScopeContext via DI to
        // snapshot Serialization.MaxArrayLength / MaxStringLength at
        // construction. ActivatorUtilities resolves the scoped
        // CacheScopeContext from _serviceProvider — same instance the
        // registry itself sees.
        Register(ActivatorUtilities.CreateInstance<BytesDataConverter>(_serviceProvider));        // 46  CacheableBytes     → byte[]
        _stringConverter = ActivatorUtilities.CreateInstance<StringDataConverter>(_serviceProvider);
        Register(_stringConverter);                                                                // 42/87/88/89 (+69 read-only) → string

        Register(ActivatorUtilities.CreateInstance<BooleanArrayDataConverter>(_serviceProvider)); // 26  BooleanArray       → bool[]
        Register(ActivatorUtilities.CreateInstance<CharArrayDataConverter>(_serviceProvider));    // 27  CharArray          → char[]
        Register(ActivatorUtilities.CreateInstance<Int16ArrayDataConverter>(_serviceProvider));   // 47  CacheableInt16Array → short[]
        Register(ActivatorUtilities.CreateInstance<Int32ArrayDataConverter>(_serviceProvider));   // 48  CacheableInt32Array → int[]
        Register(ActivatorUtilities.CreateInstance<Int64ArrayDataConverter>(_serviceProvider));   // 49  CacheableInt64Array → long[]
        Register(ActivatorUtilities.CreateInstance<SingleArrayDataConverter>(_serviceProvider));  // 50  CacheableFloatArray → float[]
        Register(ActivatorUtilities.CreateInstance<DoubleArrayDataConverter>(_serviceProvider));  // 51  CacheableDoubleArray → double[]
        // string[] and object[] both take a registry reference so
        // each element can re-enter WriteObject / ReadObject with
        // its own DSCode. Safe `this` pass — converter stores the
        // reference but doesn't invoke anything on us until Write /
        // Read fires post-construction.
        Register(new StringArrayDataConverter(this)); // 64  CacheableStringArray → string[]
        Register(new ObjectArrayDataConverter(this)); // 52  CacheableObjectArray → object[]

        // Tier B-2 collections — open-generic. Each ManagedType is
        // typeof(List<>) / typeof(HashSet<>) / typeof(Dictionary<,>);
        // WriteObject's dispatch falls back to
        // GetGenericTypeDefinition() so one converter instance handles
        // every closed instantiation. Target-shape conversion
        // (List<object?> → IList<int>, HashSet<object?> → ISet<int>,
        // Dictionary<object,object?> → Dictionary<K,V>, …) happens
        // post-decode at TypedResultAdapter, not here.
        Register(new LinkedListDataConverter(this));  // 10  CacheableLinkedList → LinkedList<T>
        Register(new ListDataConverter(this));        // 65  CacheableArrayList  → List<T>
        Register(new HashSetDataConverter(this));     // 66  CacheableHashSet    → HashSet<T>
        Register(new DictionaryDataConverter(this));  // 67  CacheableHashMap    → Dictionary<K,V>
        Register(new StackDataConverter(this));       // 74  CacheableStack      → Stack<T>
    }

    /// <summary>
    /// Add a converter to both the DSCode index (decode) and the CLR
    /// type index (encode). Built-ins only; user extension goes
    /// through <c>RegisterPdx</c> when that surface ships.
    /// </summary>
    /// <remarks>
    /// Loops <paramref name="converter"/>'s <see cref="IDataConverter.DsCodes"/>
    /// to mount every wire-form entry against the same instance —
    /// multi-DSCode converters like <c>StringDataConverter</c> need
    /// this. <see cref="_byType"/> still gets one entry per converter
    /// because the encode side keys by CLR type.
    /// </remarks>
    private void Register(IDataConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        foreach (var dsCode in converter.DsCodes)
        {
            _byDsCode[dsCode] = converter;
        }
        _byType[converter.ManagedType] = converter;
    }

    /// <summary>
    /// Snapshot of <see cref="Options.SerializationOptions.MaxArrayLength"/>.
    /// Used by the recursive collection / object-array / string-array
    /// converters, which already hold a registry reference for
    /// re-entry; the non-recursive primitive-array converters inject
    /// <see cref="CacheScopeContext"/> directly via primary ctor and
    /// snapshot independently.
    /// </summary>
    internal int MaxArrayLength { get; }

    // TODO Phase 2+: PDX path —
    //   private readonly Dictionary<string, IPdxConverter> _pdxByName = new();
    //   private readonly Dictionary<Type, IPdxConverter> _pdxByType = new();

    /// <summary>
    /// Snapshot of <see cref="Options.SerializationOptions.MaxDepth"/>
    /// at scope-build time. Read once and cached because the per-cache
    /// options bag is one-shot (<see cref="CacheScopeContext.Initialize"/>
    /// runs before any consumer resolves) and the depth check fires on
    /// every recursive write/read step — no point chasing the property
    /// chain each time.
    /// </summary>
    internal int MaxDepth { get; }

    /// <summary>
    /// Snapshot of <see cref="Options.SerializationOptions.MaxStringLength"/>.
    /// Same snapshot rationale as <see cref="MaxArrayLength"/>;
    /// consumed today only by <see cref="StringDataConverter"/> via
    /// the direct-CacheScopeContext path, but exposed here for any
    /// future recursive converter that wants to bound a nested
    /// string slot.
    /// </summary>
    internal int MaxStringLength { get; }

    /// <summary>
    /// True if <paramref name="type"/> has a registered converter
    /// (direct match or open-generic match for closed generics).
    /// Used by callers that need an early "is T a wire-supported
    /// type?" check before scheduling work that depends on the
    /// registry — e.g. <c>RemoteQueryService.NewQuery&lt;T&gt;</c>'s
    /// Phase 1.4 guard against unsupported row types.
    /// </summary>
    public bool IsRegistered(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byType.ContainsKey(type)) return true;
        if (type.IsGenericType && _byType.ContainsKey(type.GetGenericTypeDefinition())) return true;
        return false;
    }

    /// <summary>
    /// Decode one object: read the DSCode byte, dispatch to the
    /// registered converter, pass the byte back so multi-DSCode
    /// converters know which wire form to parse. Mirrors cppcache
    /// <c>DataInput::readObject()</c>.
    /// </summary>
    /// <param name="depth">
    /// Nesting level — <c>0</c> at the top-level call. Container
    /// converters re-enter with <c>depth + 1</c>; scalars don't
    /// recurse. The registry refuses payloads at
    /// <see cref="MaxDepth"/> or beyond — defends the read path
    /// against stack-overflow DoS from a malicious server payload.
    /// </param>
    /// <exception cref="GeodeException">
    /// The DSCode is not a built-in we recognise (and in Phase 2+
    /// not the PDX marker), OR <paramref name="depth"/> reached
    /// <see cref="MaxDepth"/> — wire stream more deeply nested than
    /// the client permits.
    /// </exception>
    public object? ReadObject(BigEndianBinaryReader reader, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (depth >= MaxDepth)
        {
            throw new GeodeException(
                $"SerializationRegistry: read exceeded MaxDepth ({MaxDepth}). "
                + "The server payload is more deeply nested than the client "
                + "permits — treat as hostile or buggy unless a legitimate "
                + "workload warrants it, in which case tune "
                + "GeodeClientOptions.Serialization.MaxDepth.");
        }

        var dsCode = reader.ReadByte();

        if (dsCode == DSCode.NullObj)
        {
            return null;
        }

        // TODO Phase 2+: PDX fall-through —
        //   if (dsCode == DSCode.PDX) return ReadPdx(reader);

        if (_byDsCode.TryGetValue(dsCode, out var converter))
        {
            return converter.Read(reader, dsCode, depth);
        }

        throw new GeodeException(
            $"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }

    /// <summary>
    /// Encode <paramref name="value"/>: pick a DSCode via the
    /// converter, write that byte, then delegate to the converter for
    /// the payload. Mirrors cppcache
    /// <c>DataOutput::writeObject(shared_ptr&lt;Serializable&gt;)</c>.
    /// </summary>
    /// <param name="depth">
    /// Nesting level — <c>0</c> at the top-level call. Container
    /// converters re-enter with <c>depth + 1</c>; scalars don't
    /// recurse. The registry refuses payloads at
    /// <see cref="MaxDepth"/> or beyond.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/>'s runtime type has no registered
    /// converter. Becomes a PDX fall-through in Phase 2+.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="depth"/> reached <see cref="MaxDepth"/> —
    /// likely a cycle or pathologically nested in-memory graph from
    /// the caller. Tune via
    /// <c>GeodeClientOptions.Serialization.MaxDepth</c> if the
    /// workload genuinely warrants deeper nesting.
    /// </exception>
    public void WriteObject(BigEndianBinaryWriter writer, object? value, int depth = 0)
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
            // cppcache writeObject(nullptr) → writeByte(DSCode.NullObj).
            // No payload follows.
            writer.WriteByte(DSCode.NullObj);
            return;
        }

        var type = value.GetType();
        if (TryWriteBuiltIn(writer, value, type, depth)) return;
        if (TryWritePdx(writer, value, type, depth)) return;

        throw new NotSupportedException(
            $"No SerializationRegistry converter registered for runtime type {type}.");
    }

    /// <summary>
    /// Built-in <see cref="IDataConverter"/> dispatch — closed-generic
    /// hit first, open-generic fallback (e.g. <c>List&lt;int&gt;</c>
    /// → <c>List&lt;&gt;</c>). Returns <see langword="false"/> when no
    /// built-in converter is registered for <paramref name="type"/>.
    /// </summary>
    private bool TryWriteBuiltIn(BigEndianBinaryWriter writer, object value, Type type, int depth)
    {
        if (!_byType.TryGetValue(type, out var converter)
            && type.IsGenericType)
        {
            // Open-generic fallback. Collection converters register
            // their open generic (List<>, Dictionary<,>, …) in
            // _byType; concrete instances (List<int>, List<string>,
            // …) only hit on this second lookup. Single dictionary —
            // no extra index, just a smarter probe.
            _byType.TryGetValue(type.GetGenericTypeDefinition(), out converter);
        }

        if (converter is null) return false;

        var dsCode = converter.GetDsCode(value);
        writer.WriteByte(dsCode);
        converter.Write(writer, value, dsCode, depth);
        return true;
    }

    /// <summary>
    /// PDX dispatch — encode <paramref name="value"/> when its CLR type is
    /// PDX-registered. Returns <see langword="false"/> when not registered
    /// (caller falls through to the unknown-type throw).
    /// </summary>
    /// <remarks>
    /// cppcache reference: <c>PdxHelper::serializePdx</c>
    /// (<c>PdxHelper.cpp:87-142</c>). Wire layout:
    /// <c>DSCode.PDX (1)</c> · <c>PdxLength (4 BE)</c> ·
    /// <c>TypeId (4 BE)</c> · <c>Payload (field data + var-len offset table)</c>.
    /// <para>Open design Q: <c>GetPdxIdForType</c> wire op is async;
    /// <see cref="WriteObject"/> is sync. Current path:
    /// <see cref="PdxTypeRegistry.ResolveTypeId"/> is sync,
    /// <c>SendGetPdxIdForType</c> still <c>NotImplementedException</c>
    /// (Phase 2.1 step 3b).</para>
    /// </remarks>
    private bool TryWritePdx(BigEndianBinaryWriter writer, object value, Type type, int depth)
    {
        if (!_typeRegistry.TryGetEntry(type, out var entry)) return false;

        var localWriter = new PdxLocalWriter(_stringConverter);
        entry.Write(value, localWriter);
        var (schema, payload) = localWriter.Build(entry.ClassName);

        var typeId = _pdxTypeRegistry.ResolveTypeId(schema);

        writer.WriteByte(DSCode.PDX);
        writer.WriteInt32(payload.Length + sizeof(int));   // length includes typeId field
        writer.WriteInt32(typeId);
        writer.WriteBytesOnly(payload);
        return true;
    }
}
