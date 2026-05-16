namespace Geode.Client.Internal;

/// <summary>
/// A Geode server's network location (host + port). Mirrors cppcache
/// <c>ServerLocation</c> (<c>cppcache/src/ServerLocation.hpp</c>) — the
/// wire-layer value type returned by locator-protocol responses
/// (<c>LocatorListResponse</c>, <c>GetAllServersResponse</c>).
/// </summary>
/// <remarks>
/// Distinct from <see cref="System.Net.DnsEndPoint"/> (runtime endpoint
/// identity, no wire codec) and
/// <see cref="Geode.Client.Options.CacheHostPortOptions"/> (options
/// layer, JSON-bindable). cppcache <c>ServerGroup</c> is **not** a
/// field of <c>ServerLocation</c> — it is a separate parameter at the
/// call site (e.g. <c>updateLocators(serverGrp)</c>).
/// DataSerializable wire codec lands in Step B of the locator helper
/// roadmap (see <c>ThinClientPoolDM.UpdateLocatorsLocalAsync</c>).
/// </remarks>
internal sealed record ServerLocation(string Host, int Port);
