namespace Geode.Client.Internal;

/// <summary>
/// LRU-variant <see cref="MapEntry"/> — carries per-entry
/// <see cref="LRUEntryProperties"/> (overflow persistence handle + LRU-list
/// node). Produced by <see cref="LRUEntryFactory"/> for LRU-bounded regions.
/// Mirrors cppcache <c>LRUMapEntry</c> (<c>cppcache/src/LRUMapEntry.hpp:64</c>).
/// </summary>
/// <remarks>
/// cppcache <c>LRUMapEntry : public MapEntryImpl, public LRUEntryProperties</c>
/// 多重繼承 — 自身就是 <c>LRUEntryProperties</c>,<c>getLRUProperties()</c> 回
/// <c>*this</c>。C# 無 MI,改用組合(持有一個 <see cref="LRUEntryProperties"/>
/// 實例),override <see cref="MapEntry.LRUProperties"/> 回該實例 — 跟
/// <see cref="VersionedMapEntryImpl"/> 持 <see cref="VersionStamp"/> 同套路。
/// <para>
/// cppcache <c>LRUMapEntry::cleanup</c> override 內是「非 eviction 才從 LRU
/// list 解開」,但 cppcache 自己留 TODO(MI 移除後的雙向鏈結未實作)。我們的
/// LRU queue 是 <see cref="LRUEntriesMap"/> 外掛的 <see cref="LRUQueue"/>
/// (在 map 層 pop / remove),不靠 entry 自解,所以這裡沿用 base
/// <see cref="MapEntryImpl.Cleanup"/> 的 no-op,不另 override。
/// </para>
/// </remarks>
internal class LRUMapEntry(object key) : MapEntryImpl(key)
{
    /// <summary>
    /// Composed <see cref="LRUEntryProperties"/> (cppcache 多重繼承
    /// <c>LRUMapEntry : MapEntryImpl, LRUEntryProperties</c> → 組合)。
    /// </summary>
    public override LRUEntryProperties LRUProperties { get; } = new();
}

/// <summary>
/// Versioned LRU-variant entry — adds a per-entry <see cref="VersionStamp"/>
/// on top of <see cref="LRUMapEntry"/>. Produced by
/// <see cref="LRUEntryFactory"/> when concurrency-checks are enabled. Mirrors
/// cppcache <c>VersionedLRUMapEntry</c>
/// (<c>cppcache/src/LRUMapEntry.hpp:89</c>).
/// </summary>
/// <remarks>
/// cppcache <c>VersionedLRUMapEntry : public LRUMapEntry, public VersionStamp</c>
/// 多重繼承 → 組合(持 <see cref="VersionStamp"/> 實例),override
/// <see cref="MapEntry.VersionStamp"/> 回它。
/// </remarks>
internal sealed class VersionedLRUMapEntry(object key) : LRUMapEntry(key)
{
    /// <summary>Composed <see cref="VersionStamp"/>(cppcache MI → 組合)。</summary>
    public override VersionStamp VersionStamp { get; } = new();
}
