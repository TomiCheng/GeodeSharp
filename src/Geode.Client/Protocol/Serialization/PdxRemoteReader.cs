using Geode.Client.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Decodes a PDX object's field payload when the remote schema has fields
/// the local class doesn't know about. Extends <see cref="PdxLocalReader"/>
/// to also capture the unread bytes for later
/// <c>PdxTypeRegistry.SetPreserveData</c> (cross-language schema evolution).
/// Mirror of cppcache <c>PdxRemoteReader</c>
/// (<c>cppcache/src/PdxRemoteReader.hpp</c>).
/// </summary>
/// <remarks>
/// For walking-skeleton this is a thin shell — methods inherit from
/// <see cref="PdxLocalReader"/>. cppcache overrides every <c>readXxx</c>
/// to walk fields by remote index (<c>m_currentIndex</c>) and use the
/// schema's <c>RemoteToLocalFieldMap</c> to resolve field positions when
/// local and remote schemas diverge. TODO: port that once a real
/// schema-divergence scenario appears.
/// </remarks>
internal sealed class PdxRemoteReader(
    IServiceProvider serviceProvider,
    GeodeCache cache,
    PdxType pdxType,
    DataInput input,
    int pdxLength)
    : PdxLocalReader(serviceProvider, cache, pdxType, input, pdxLength)
{
    static readonly ObjectFactory<PdxRemoteReader> _factory
        = ActivatorUtilities.CreateFactory<PdxRemoteReader>(
            [typeof(GeodeCache), typeof(PdxType), typeof(DataInput), typeof(int)]);

    public static new PdxRemoteReader Create(IServiceProvider serviceProvider,
        GeodeCache cache, PdxType pdxType, DataInput input, int pdxLength)
        => _factory(serviceProvider, [cache, pdxType, input, pdxLength]);
}
