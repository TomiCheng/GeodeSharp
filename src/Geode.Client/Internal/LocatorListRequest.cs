/*
using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Locator wire request: "give me the current locator set, filtered
/// by server group". Mirrors cppcache <c>LocatorListRequest</c>
/// (<c>cppcache/src/LocatorListRequest.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// Wire tag: <see cref="DSFid.LocatorListRequest"/>. Body is one
/// Java-modified-UTF-8 length-prefixed string (the server group;
/// empty selects all servers, matching cppcache default). The outer
/// locator frame (gossip version + Geode version + DSCode-tagged
/// FixedId envelope) is the <c>LocatorConnection</c>'s responsibility
/// (Step C). cppcache's <c>fromData</c> is intentionally empty ??/// locator requests are client-to-server only, never deserialized on
/// the receiving side.
/// </remarks>
internal sealed record LocatorListRequest(string ServerGroup = "")
{
    /// <summary>
    /// Write the request body. Mirrors cppcache
    /// <c>LocatorListRequest::toData</c>
    /// (<c>LocatorListRequest.cpp:32-34</c>): a single
    /// <c>writeString(m_servergroup)</c>.
    /// </summary>
    public void WriteTo(DataOutput writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(ServerGroup);
    }
}

*/