namespace Geode.Client.Internal;

/// <summary>
/// Non-versioned <see cref="MapEntry"/> implementation — value + key
/// storage without version-stamp tracking. Mirrors cppcache
/// <c>MapEntryImpl</c>
/// (<c>cppcache/src/MapEntryImpl.hpp:49-105</c>); cppcache
/// <c>MapEntryImpl::getVersionStamp</c> 拋
/// <c>IllegalStateException("called for non-versioned MapEntry")</c> —
/// 對應 sibling <see cref="VersionedMapEntryImpl"/> 才會帶
/// <see cref="VersionStamp"/>。
/// Skeleton only — Key / Value / cleanup / LRU / Exp properties land
/// with the caching-enabled phase (Phase 2+).
/// </summary>
internal class MapEntryImpl : MapEntry
{
    /// <inheritdoc />
    public override object? Value { get; set; }
}
