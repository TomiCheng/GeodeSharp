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
    private readonly Dictionary<byte, IDataConverter> _byDsCode = new();
    private readonly Dictionary<Type, IDataConverter> _byType = new();

    // TODO Phase 2+: PDX path —
    //   private readonly Dictionary<string, IPdxConverter> _pdxByName = new();
    //   private readonly Dictionary<Type, IPdxConverter> _pdxByType = new();

    public SerializationRegistry()
    {
        // Built-in converters. cppcache registers ~30 of these at
        // SerializationRegistry construction; we add them as their
        // wire formats land. Phase 1.2 shipped int32 + boolean (the
        // walking-skeleton minimum); Phase 1.3.0 widened to the full
        // Tier A scalar / bytes / string set; Phase 1.3.d adds the
        // primitive-array tier (one per primitive + string[]).
        // Order: scalar (sorted by DSCode), then bytes, then string,
        // then arrays (sorted by DSCode).
        Register(new BooleanDataConverter());      // 53  CacheableBoolean   → bool
        Register(new CharacterDataConverter());    // 54  CacheableCharacter → char
        Register(new ByteDataConverter());         // 55  CacheableByte      → byte (unsigned, .NET convention)
        Register(new Int16DataConverter());        // 56  CacheableInt16     → short
        Register(new Int32DataConverter());        // 57  CacheableInt32     → int
        Register(new Int64DataConverter());        // 58  CacheableInt64     → long
        Register(new SingleDataConverter());       // 59  CacheableFloat     → float
        Register(new DoubleDataConverter());       // 60  CacheableDouble    → double
        Register(new DateTimeDataConverter());     // 61  CacheableDate      → DateTime
        Register(new BytesDataConverter());        // 46  CacheableBytes     → byte[]
        Register(new StringDataConverter());       // 42/87/88/89 (+69 read-only) → string

        Register(new BooleanArrayDataConverter()); // 26  BooleanArray       → bool[]
        Register(new CharArrayDataConverter());    // 27  CharArray          → char[]
        Register(new Int16ArrayDataConverter());   // 47  CacheableInt16Array → short[]
        Register(new Int32ArrayDataConverter());   // 48  CacheableInt32Array → int[]
        Register(new Int64ArrayDataConverter());   // 49  CacheableInt64Array → long[]
        Register(new SingleArrayDataConverter());  // 50  CacheableFloatArray → float[]
        Register(new DoubleArrayDataConverter());  // 51  CacheableDoubleArray → double[]
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
    /// Encode <paramref name="value"/>: pick a DSCode via the
    /// converter, write that byte, then delegate to the converter for
    /// the payload. Mirrors cppcache
    /// <c>DataOutput::writeObject(shared_ptr&lt;Serializable&gt;)</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/>'s runtime type has no registered
    /// converter. Becomes a PDX fall-through in Phase 2+.
    /// </exception>
    public void WriteObject(BigEndianBinaryWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            // cppcache writeObject(nullptr) → writeByte(DSCode.NullObj).
            // No payload follows.
            writer.WriteByte(DSCode.NullObj);
            return;
        }

        var type = value.GetType();
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

        if (converter is not null)
        {
            var dsCode = converter.GetDsCode(value);
            writer.WriteByte(dsCode);
            converter.Write(writer, value, dsCode);
            return;
        }

        // TODO Phase 2+: PDX fall-through —
        //   if (_pdxByType.TryGetValue(type, out var pdx))
        //   {
        //       writer.WriteByte(DSCode.PDX);
        //       WritePdx(writer, value, pdx);
        //       return;
        //   }

        throw new NotSupportedException(
            $"No SerializationRegistry converter registered for runtime type {type}.");
    }

    /// <summary>
    /// Decode one object: read the DSCode byte, dispatch to the
    /// registered converter, pass the byte back so multi-DSCode
    /// converters know which wire form to parse. Mirrors cppcache
    /// <c>DataInput::readObject()</c>.
    /// </summary>
    /// <exception cref="GeodeException">
    /// The DSCode is not a built-in we recognise and (in Phase 2+)
    /// not the PDX marker.
    /// </exception>
    public object? ReadObject(BigEndianBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var dsCode = reader.ReadByte();

        if (dsCode == DSCode.NullObj)
        {
            return null;
        }

        // TODO Phase 2+: PDX fall-through —
        //   if (dsCode == DSCode.PDX) return ReadPdx(reader);

        if (_byDsCode.TryGetValue(dsCode, out var converter))
        {
            return converter.Read(reader, dsCode);
        }

        throw new GeodeException(
            $"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }
}
