/*
using Geode.Client.Protocol;

namespace Geode.Client.Internal;

/// <summary>
/// Locator wire response: the cluster's current authoritative locator
/// set, plus a hint flag. Mirrors cppcache <c>LocatorListResponse</c>
/// (<c>cppcache/src/LocatorListResponse.hpp/.cpp</c>).
/// </summary>
/// <remarks>
/// Wire tag: <see cref="DSFid.LocatorListResponse"/>. Two fields:
/// <see cref="Locators"/> (the new locator list — replaces the
/// client's working set after a merge step in Step D) and
/// <see cref="IsBalanced"/> (a server-side load-balancer hint,
/// currently unused on the client). Wire body is <c>u32 count</c>
/// followed by that many <c>(host, port)</c> pairs, then a <c>bool</c>.
/// The outer frame (gossip / Geode version / DSCode envelope) is the
/// <c>LocatorConnection</c>'s job (Step C).
/// </remarks>
internal sealed record LocatorListResponse(
    IReadOnlyList<ServerLocation> Locators,
    bool IsBalanced)
{
    /// <summary>
    /// Decode the response body. Mirrors cppcache
    /// <c>LocatorListResponse::fromData</c>
    /// (<c>LocatorListResponse.cpp:30-33</c>) + <c>readList</c>
    /// (<c>LocatorListResponse.cpp:39-46</c>): <c>u32 count</c> followed
    /// by <c>count</c> <c>ServerLocation</c> pairs, then a <c>bool</c>.
    /// Each <c>ServerLocation</c> reads <c>readString(host) +
    /// readInt32(port)</c> per cppcache <c>ServerLocation::fromData</c>
    /// (<c>ServerLocation.hpp:73-77</c>).
    /// </summary>
    public static LocatorListResponse ReadFrom(BigEndianBinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new GeodeException(
                $"LocatorListResponse: negative locator count {count} — wire corruption.");
        }

        var locators = new List<ServerLocation>(count);
        for (var i = 0; i < count; i++)
        {
            // cppcache ServerLocation.fromData: readString + readInt32.
            // Host is never legitimately null on the wire; coerce a stray
            // CacheableNullString DSCode to empty rather than crashing the
            // decode mid-list.
            var host = reader.ReadString() ?? string.Empty;
            var port = reader.ReadInt32();
            locators.Add(new ServerLocation(host, port));
        }

        var isBalanced = reader.ReadBool();
        return new LocatorListResponse(locators, isBalanced);
    }
}

*/