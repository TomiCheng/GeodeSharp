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
/// <b>Per-cache scope.</b> Registered as DI Scoped alongside
/// <see cref="Services.Cache"/> so multi-cluster setups can have
/// different custom-type registrations per cluster without leaking
/// across.
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
        // wire formats land. Phase 1.2 starts with int32 (the
        // walking-skeleton key type).
        Register(new Int32DataConverter());

        // TODO Phase 1.2.c: widen the built-in set —
        //   Register(new BooleanDataConverter());
        //   Register(new ByteDataConverter());
        //   Register(new Int16DataConverter());
        //   Register(new Int64DataConverter());
        //   Register(new SingleDataConverter());
        //   Register(new DoubleDataConverter());
        //   Register(new StringDataConverter());      // multi-DSCode (CacheableString / ASCII / Huge)
        //   Register(new BytesDataConverter());       // CacheableBytes with IsObject toggle
        //   Register(new DateTimeDataConverter());
        //   Register(new <collection codecs>);        // List / Dictionary / HashSet / arrays
    }

    /// <summary>
    /// Add a converter to both the DSCode index (decode) and the CLR
    /// type index (encode). Built-ins only; user extension goes
    /// through <c>RegisterPdx</c> when that surface ships.
    /// </summary>
    private void Register(IDataConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _byDsCode[converter.DsCode] = converter;
        _byType[converter.ManagedType] = converter;
    }

    /// <summary>
    /// Encode <paramref name="value"/>: write its DSCode byte then
    /// delegate to the registered converter for the payload. Mirrors
    /// cppcache <c>DataOutput::writeObject(shared_ptr&lt;Serializable&gt;)</c>.
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
        if (_byType.TryGetValue(type, out var converter))
        {
            writer.WriteByte(converter.DsCode);
            converter.Write(writer, value);
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
    /// registered converter. Mirrors cppcache
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
            return converter.Read(reader);
        }

        throw new GeodeException(
            $"SerializationRegistry: unknown DSCode {dsCode} on the wire.");
    }
}
