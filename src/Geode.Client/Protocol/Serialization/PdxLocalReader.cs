using Geode.Client.Internal;
using Geode.Client.Pdx;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Decodes a PDX object's field payload using a local schema. Mirror of
/// cppcache <c>PdxLocalReader</c> (<c>cppcache/src/PdxLocalReader.hpp</c>).
/// </summary>
/// <remarks>
/// Wire layout (excluding the leading <c>DSCode.PDX</c> + <c>pdxLength</c> +
/// <c>typeId</c> already consumed by <c>SerializationRegistry.ReadPdxAsync</c>):
/// <code>
///   &lt;field data, in schema order — fixed inline, var-len inline too&gt;
///   &lt;offset table at end, reverse order; first var-len omitted&gt;
/// </code>
/// Field random-access at read time: schema's <c>RelativeOffset</c> picks
/// fixed-size positions; <c>VarLenOffsetIndex</c> picks slots in the
/// trailing offset table (size 1/2/4 bytes per entry depending on
/// <c>pdxLength</c>, cppcache <c>PdxLocalReader::initialize</c>).
/// </remarks>
internal class PdxLocalReader(
    IServiceProvider serviceProvider,
    GeodeCache cache,
    PdxType pdxType,
    DataInput input,
    int pdxLength) : IPdxReader
{
    protected readonly IServiceProvider _serviceProvider = serviceProvider;
    protected readonly GeodeCache _cache = cache;
    protected readonly PdxType _pdxType = pdxType;
    protected readonly DataInput _input = input;
    protected readonly int _pdxLength = pdxLength;

    static readonly ObjectFactory<PdxLocalReader> _factory
        = ActivatorUtilities.CreateFactory<PdxLocalReader>(
            [typeof(GeodeCache), typeof(PdxType), typeof(DataInput), typeof(int)]);

    public static PdxLocalReader Create(IServiceProvider serviceProvider,
        GeodeCache cache, PdxType pdxType, DataInput input, int pdxLength)
        => _factory(serviceProvider, [cache, pdxType, input, pdxLength]);

    // Sequential reads — mirror cppcache PdxLocalReader.cpp:99-205 where
    // every readXxx(const std::string&) ignores the field name and just
    // pulls from the buffer in declaration order. Local-schema invariant:
    // the user's FromData mirrors ToData's field order, so the cursor is
    // always at the right offset. Random-access (offset-table seek) is
    // PdxRemoteReader's job, not ours.

    public virtual bool ReadBoolean(string fieldName) => _input.ReadBool();

    public virtual sbyte ReadByte(string fieldName) => _input.ReadSByte();

    public virtual char ReadChar(string fieldName) => (char)_input.ReadUInt16();

    public virtual short ReadShort(string fieldName) => _input.ReadInt16();

    public virtual int ReadInt(string fieldName) => _input.ReadInt32();

    public virtual long ReadLong(string fieldName) => _input.ReadInt64();

    public virtual float ReadFloat(string fieldName) => _input.ReadFloat();

    public virtual double ReadDouble(string fieldName) => _input.ReadDouble();

    public virtual string? ReadString(string fieldName) => _input.ReadString();

    public virtual DateTime ReadDate(string fieldName)
    {
        // Java Date wire form: signed int64 ms since Unix epoch.
        // DateTime.UnixEpoch is Kind=Utc; AddTicks preserves Kind so we
        // hand back a UTC DateTime (matches DateTimeDataConverter.Read).
        var ms = _input.ReadInt64();
        return DateTime.UnixEpoch.AddTicks(ms * TimeSpan.TicksPerMillisecond);
    }
}
