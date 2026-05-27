namespace Geode.Client;

/// <summary>
/// Post-creation mutator for a region's runtime-tweakable attributes
/// (expiration timeouts, LRU limit, etc.). Mirrors cppcache
/// <c>AttributesMutator</c>
/// (<c>cppcache/include/geode/AttributesMutator.hpp</c>).
/// </summary>
/// <remarks>
/// Empty marker today — setters (<c>SetEntryTimeToLive</c> /
/// <c>SetEntryIdleTimeout</c> / <c>SetRegionTimeToLive</c> /
/// <c>SetLruEntriesLimit</c> / <c>SetCacheListener</c> /
/// <c>SetCacheLoader</c> / <c>SetCacheWriter</c>) land when expiration /
/// listener wiring ships (Phase 2+).
/// </remarks>
public interface IAttributesMutator
{
}
