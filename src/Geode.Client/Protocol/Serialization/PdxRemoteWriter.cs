namespace Geode.Client.Protocol.Serialization;

/// <summary>
/// Encodes a PDX object's payload using a remote (server-known) <c>PdxType</c>'s
/// field layout, plus any preserved unread fields. Mirror of cppcache
/// <c>PdxRemoteWriter</c> (<c>cppcache/src/PdxRemoteWriter.hpp</c>).
/// </summary>
internal sealed class PdxRemoteWriter(IServiceProvider serviceProvider)
    : PdxLocalWriter(serviceProvider)
{
}
