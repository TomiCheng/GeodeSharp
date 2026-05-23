/*
using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Locator wire request: "give me a server for a new forward (client ??/// server) connection". Mirrors cppcache
/// <c>ClientConnectionRequest</c>
/// (<c>cppcache/src/ClientConnectionRequest.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// Wire tag: <see cref="DSFid.ClientConnectionRequest"/>. Body is
/// <c>writeString(serverGroup)</c> followed by an i32 count of
/// excluded server locations, each serialised as cppcache
/// <c>ServerLocation::toData</c> (<c>writeString(host) +
/// writeInt(port)</c>). The outer locator frame (gossip version +
/// Geode version + DSCode-tagged FixedId envelope) is the
/// <see cref="LocatorConnection"/>'s responsibility.
/// </remarks>
internal sealed record ClientConnectionRequest(
    string ServerGroup,
    IReadOnlyCollection<ServerLocation> ExcludedServers)
{
    /// <summary>Mirrors cppcache <c>ClientConnectionRequest::toData</c> (<c>ClientConnectionRequest.cpp:27-30</c>) + <c>writeSetOfServerLocation</c> (<c>:36-46</c>).</summary>
    public void WriteTo(DataOutput writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(ServerGroup);

        writer.WriteInt32(ExcludedServers.Count);
        foreach (var loc in ExcludedServers)
        {
            // cppcache ServerLocation::toData (ServerLocation.hpp:68-71)
            writer.WriteString(loc.Host);
            writer.WriteInt32(loc.Port);
        }
    }
}

*/