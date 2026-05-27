using Geode.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Encodes a PDX object while simultaneously collecting its field layout into
/// a fresh <c>PdxType</c> (used on first serialization of an unknown type).
/// Mirror of cppcache <c>PdxWriterWithTypeCollector</c>
/// (<c>cppcache/src/PdxWriterWithTypeCollector.hpp</c>).
/// </summary>
internal sealed class PdxWriterWithTypeCollector(IServiceProvider serviceProvider, string className)
    : PdxLocalWriter(serviceProvider)
{

    static readonly ObjectFactory<PdxWriterWithTypeCollector> _factory
        = ActivatorUtilities.CreateFactory<PdxWriterWithTypeCollector>([typeof(string)]);

    public static PdxWriterWithTypeCollector Create(IServiceProvider serviceProvider, string className)
    {
        return _factory(serviceProvider, [className]);
    }

    public string ClassName => className;

    public PdxType GetPdxLocalType() => BuildSchema(className);
}
