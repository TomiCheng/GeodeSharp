namespace Geode.Client.Internal;

/// <summary>
/// Per-key entry held inside <see cref="EntriesMap"/>: value plus
/// version / tracker / tombstone metadata. Mirrors cppcache
/// <c>MapEntryImpl</c> (<c>cppcache/src/MapEntry.hpp</c>). Skeleton only
/// — members land with the caching-enabled phase (Phase 2+).
/// </summary>
internal sealed class MapEntry
{
}
