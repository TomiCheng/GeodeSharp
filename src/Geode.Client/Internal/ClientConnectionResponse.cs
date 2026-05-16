using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Locator wire response to <see cref="ClientConnectionRequest"/>: a
/// single server location (host/port) plus a "found" flag. Mirrors
/// cppcache <c>ClientConnectionResponse</c>
/// (<c>cppcache/src/ClientConnectionResponse.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// Wire tag: <see cref="DSFid.ClientConnectionResponse"/>. Body is a
/// <c>bool serverFound</c>; when <see cref="ServerFound"/> is
/// <see langword="true"/>, the bool is followed by a single
/// <see cref="ServerLocation"/> pair (<c>readString(host) +
/// readInt32(port)</c>). When <see langword="false"/>, the locator
/// reachability succeeded but the cluster currently has no server
/// matching the requested group — caller should treat it differently
/// from a transport error (cppcache <c>locatorFound=true</c> branch in
/// <c>getEndpointForNewFwdConn</c>).
/// </remarks>
internal sealed record ClientConnectionResponse(
    bool ServerFound,
    ServerLocation? Server)
{
    /// <summary>Mirrors cppcache <c>ClientConnectionResponse::fromData</c> (<c>ClientConnectionResponse.cpp:28-33</c>).</summary>
    public static ClientConnectionResponse ReadFrom(BigEndianBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var serverFound = reader.ReadBool();
        if (!serverFound)
        {
            return new ClientConnectionResponse(ServerFound: false, Server: null);
        }

        // cppcache ServerLocation::fromData: readString + readInt32.
        var host = reader.ReadString() ?? string.Empty;
        var port = reader.ReadInt32();
        return new ClientConnectionResponse(
            ServerFound: true,
            Server: new ServerLocation(host, port));
    }
}
