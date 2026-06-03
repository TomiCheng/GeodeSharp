using System.Collections.ObjectModel;
using Geode.Client.Internal;
using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol;

/// <summary>
/// Factory for the TCR request frames (<see cref="TcrMessage"/>) each
/// Geode operation puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// One method per <see cref="MessageType"/>, each in its own partial
/// file (<c>TcrMessageBuilder.Ping.cs</c>, <c>TcrMessageBuilder.Put.cs</c>,
/// ...). The collection mirrors cppcache's <c>TcrMessage.hpp</c> family
/// of <c>TcrMessage*</c> subclasses (<c>TcrMessagePing</c>,
/// <c>TcrMessagePut</c>, <c>TcrMessageRequest</c>, ...) ??same
/// per-operation recipe, expressed as functions returning an immutable
/// <see cref="TcrMessage"/> rather than as a class hierarchy.
/// </para>
/// <para>
/// Pure functions: no I/O, no hidden state. The "send it + handle the
/// reply" half lives on <see cref="TcrConnection"/> as op methods
/// (<see cref="TcrConnection.PingAsync"/> etc.); callers can also
/// compose <see cref="TcrConnection.SendRequestAsync"/> with a builder
/// result directly when they want full control over reply dispatch.
/// </para>
/// <para>
/// Every <see cref="TcrMessage"/> header stamps the ambient
/// <see cref="TSSTXStateWrapper.Current"/>'s transaction id (or <c>-1</c>
/// when none), read at <see cref="BuildAsync"/> time. Mirrors cppcache
/// <c>TcrMessage::writeHeader</c> peeking
/// <c>TSSTXStateWrapper::get().getTXState()</c>.
/// </para>
/// </remarks>
internal sealed partial class TcrMessageBuilder(IServiceProvider serviceProvider, MessageType messageType)
{
    byte _earlyAck = 0;
    readonly List<TcrPartBuilder> _tcrPartBuilders = [];
    readonly SerializationRegistry _serializationRegistry = serviceProvider.GetRequiredService<SerializationRegistry>();

    internal IServiceProvider ServiceProvider => serviceProvider;
    internal SerializationRegistry SerializationRegistry => _serializationRegistry;

    public static TcrMessageBuilder Create(IServiceProvider serviceProvider, MessageType messageType)
    {
        return new TcrMessageBuilder(serviceProvider, messageType);
    }

    public TcrMessageBuilder AddPart(Func<CancellationToken, ValueTask<TcrPart>> func)
    {
        _tcrPartBuilders.Add(new TcrPartBuilder(func));
        return this;
    }

    public TcrMessageBuilder AddKeepAlivePart(bool value)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.KeepAlive(value));
        return this;
    }

    public TcrMessageBuilder AddRegionNamePart(string regionName)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.RegionName(serviceProvider, regionName));
        return this;
    }

    public TcrMessageBuilder AddKeyPart(object key)
    {
        return AddPart(async (ct) =>
        {
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
            await _serializationRegistry.WriteObjectAsync(output, key, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }

    public TcrMessageBuilder AddInt32Part(int value)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.Int32(serviceProvider, value));
        return this;
    }

    public TcrMessageBuilder AddNullObjectPart()
    {
        _tcrPartBuilders.Add(TcrPartBuilder.NullObj());
        return this;
    }

    /// <summary>
    /// Add a <see cref="DSCode.CacheableBoolean"/>-tagged 1-byte part
    /// (<c>IsObject=1</c>). Used for the <c>isDelta</c> slot in Put;
    /// mirrors cppcache <c>writeObjectPart(CacheableBoolean::create(...))</c>.
    /// </summary>
    public TcrMessageBuilder AddCacheableBooleanPart(bool value)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.CacheableBoolean(value));
        return this;
    }

    /// <summary>
    /// Add a DSCode-tagged serialized <paramref name="value"/> part
    /// (<c>IsObject=1</c>); structurally identical to
    /// <see cref="AddKeyPart"/>, named separately for caller semantics.
    /// Mirrors cppcache <c>writeObjectPart(value, isDelta)</c> minus
    /// delta support (Phase 4+).
    /// </summary>
    public TcrMessageBuilder AddValuePart(object? value)
    {
        return AddPart(async ct =>
        {
            using var output = ActivatorUtilities.CreateInstance<DataOutput>(serviceProvider);
            await _serializationRegistry.WriteObjectAsync(output, value, ct: ct);
            return new TcrPart(IsObject: 1, output.WrittenSpan.ToArray());
        });
    }

    /// <summary>
    /// Add the 18-byte EventId part (<c>IsObject=0</c>) mirroring
    /// cppcache <c>writeEventIdPart</c>
    /// (<c>cppcache/src/TcrMessage.cpp:834</c>): longCode-tagged
    /// <paramref name="threadId"/> + <paramref name="sequenceId"/>,
    /// both <see cref="long"/> BE. Pair sourced from
    /// <see cref="Services.EventIdGenerator.Next"/>.
    /// </summary>
    public TcrMessageBuilder AddEventIdPart(long threadId, long sequenceId)
    {
        _tcrPartBuilders.Add(TcrPartBuilder.EventId(serviceProvider, threadId, sequenceId));
        return this;
    }

    public async ValueTask<TcrMessage> BuildAsync(CancellationToken ct = default)
    {
        var parts = new List<TcrPart>();
        foreach (var builder in _tcrPartBuilders)
        {
            parts.Add(await builder.BuildAsync(ct));
        }

        // Direct construction (record positional ctor) rather than
        // ActivatorUtilities — BuildAsync is called from CloseAsync on the
        // sp-teardown path, and ActivatorUtilities would re-enter the
        // disposing ServiceProvider to resolve `IServiceProvider`, throwing
        // ObjectDisposedException. We already hold every ctor arg.
        var transactionId = TSSTXStateWrapper.Current?.TransactionId.Id ?? -1;
        return new TcrMessage(serviceProvider, messageType, transactionId, _earlyAck, parts);

    }
}

