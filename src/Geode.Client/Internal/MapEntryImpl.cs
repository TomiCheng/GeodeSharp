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
/// with the caching-enabled work.
/// </summary>
internal class MapEntryImpl : MapEntry
{
    /// <inheritdoc />
    public override object? Value { get; set; }

    /// <summary>
    /// Non-versioned entry has no stamp. Mirrors cppcache
    /// <c>MapEntryImpl::getVersionStamp</c>
    /// (<c>cppcache/src/MapEntryImpl.hpp</c>) which throws
    /// <c>IllegalStateException("called for non-versioned MapEntry")</c>;
    /// per the BCL exception policy that's <see cref="InvalidOperationException"/>.
    /// </summary>
    public override VersionStamp VersionStamp =>
        throw new InvalidOperationException(
            "VersionStamp called for non-versioned MapEntry.");

    /// <summary>
    /// Tracker counter backing field. cppcache 用 <c>MapEntryT&lt;TBase, N, U&gt;</c>
    /// 模板 + placement-new 改 vptr 來「攜帶」<c>UPDATE_COUNT</c>;C# 攤平
    /// 成一個普通 int field,所有 non-versioned / versioned entries 都用它。
    /// </summary>
    private int _updateCount;

    /// <inheritdoc />
    public override int UpdateCount => _updateCount;

    /// <inheritdoc />
    public override void IncrementUpdateCount() => _updateCount++;

    /// <inheritdoc />
    /// <remarks>
    /// 非 LRU entry 沒事做。對映 cppcache
    /// <c>MapEntryImpl::cleanup(CacheEventFlags) override {}</c>(空 body)。
    /// LRU-variant entry 才會 override 去從 LRU queue 解開。
    /// </remarks>
    public override void Cleanup(CacheEventFlags eventFlags) { }
}
