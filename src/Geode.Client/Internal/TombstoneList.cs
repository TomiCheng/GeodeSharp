namespace Geode.Client.Internal;

/// <summary>
/// Per-region list of tombstone entries (deleted-but-not-yet-collected)
/// kept for distributed concurrency-checks version history. Mirrors
/// cppcache <c>TombstoneList</c>
/// (<c>cppcache/src/TombstoneList.hpp:40</c>). Skeleton only — members
/// (erase / add / iterate / GC) land with the concurrency-checks +
/// tombstone GC subsystem (Phase 2+).
/// </summary>
internal sealed class TombstoneList
{
    /// <summary>
    /// 把 <paramref name="key"/> 的 tombstone 從 list 移除,順便取消對應的
    /// GC task(若 <paramref name="cancelTask"/> 為 <see langword="true"/>)。
    /// 回 <see langword="true"/> 表 key 原本存在於 list、已成功移除。Mirrors
    /// cppcache <c>TombstoneList::erase</c>
    /// (<c>cppcache/src/TombstoneList.hpp:47</c>)。
    /// </summary>
    public bool Erase(object key, bool cancelTask = true) => throw new NotImplementedException();
}
