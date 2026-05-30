namespace Geode.Client.Internal;

/// <summary>
/// MapEntry variant that carries explicit tracker counters
/// (<see cref="TrackingNumber"/> + <see cref="UpdateCount"/>) for the
/// optimistic-concurrency tracker subsystem. Mirrors cppcache
/// <c>TrackedMapEntry</c> (<c>cppcache/src/TrackedMapEntry.hpp</c>) —
/// in cppcache it's the boundary fallback when the placement-new
/// template machinery in <c>MapEntryT</c> hits <c>UPDATE_MAX</c> /
/// <c>TRACK_MAX</c>. C# 沒有 placement-new morph,這 class 直接當
/// 所有 tracker-active entry 的 wrapper — 走的時機由 caller
/// (<c>AddTrackerForEntry</c> / <see cref="IncrementUpdateCount"/>)決定。
/// </summary>
/// <remarks>
/// 大部分 surface 是 delegate 到內部 <see cref="MapEntryImpl"/>;唯
/// <see cref="VersionStamp"/> 拋 — tracker 與版本控制是互斥的兩種
/// concurrency 機制,一個 entry 不會同時走兩條路。對映 cppcache
/// <c>TrackedMapEntry::getVersionStamp</c> 拋
/// <c>FatalInternalException("MapEntry::getVersionStamp for TrackedMapEntry is not applicable")</c>。
/// </remarks>
internal sealed class TrackedMapEntry(
    MapEntryImpl entry, int trackingNumber, int updateCount) : MapEntry
{
    private readonly MapEntryImpl _entry = entry;
    private int _trackingNumber = trackingNumber;
    private int _updateCount = updateCount;

    /// <summary>Inner <see cref="MapEntryImpl"/> handle. Mirrors cppcache <c>m_entry</c> / <c>getImplPtr()</c>.</summary>
    internal MapEntryImpl InnerEntry => _entry;

    /// <summary>Active tracker count. Mirrors cppcache <c>m_trackingNumber</c> / <c>getTrackingNumber()</c>.</summary>
    public int TrackingNumber => _trackingNumber;

    /// <inheritdoc />
    public override int UpdateCount => _updateCount;

    /// <inheritdoc />
    public override object? Value
    {
        get => _entry.Value;
        set => _entry.Value = value;
    }

    /// <summary>
    /// cppcache <c>TrackedMapEntry::addTracker</c>: <c>++m_trackingNumber;
    /// return m_updateCount;</c>. cppcache 的 <c>shared_ptr&amp;</c>
    /// out-param 在這個 override 沒寫入,C# 收掉。
    /// </summary>
    public int AddTracker()
    {
        _trackingNumber++;
        return _updateCount;
    }

    /// <summary>
    /// cppcache <c>TrackedMapEntry::removeTracker</c>: 計數正才減;歸零時
    /// 連帶把 <see cref="UpdateCount"/> 也歸零。回傳 <c>(reachedZero,
    /// trackingNumber)</c>。
    /// </summary>
    public (bool ReachedZero, int TrackingNumber) RemoveTracker()
    {
        if (_trackingNumber > 0)
        {
            _trackingNumber--;
        }
        if (_trackingNumber == 0)
        {
            _updateCount = 0;
            return (true, 0);
        }
        return (false, _trackingNumber);
    }

    /// <inheritdoc />
    /// <remarks>
    /// cppcache <c>TrackedMapEntry::incrementUpdateCount</c>:
    /// <c>return ++m_updateCount;</c>。C# 的 <see cref="MapEntry.IncrementUpdateCount"/>
    /// 簽章是 void,return 值收掉。
    /// </remarks>
    public override void IncrementUpdateCount() => _updateCount++;

    /// <inheritdoc />
    /// <remarks>cppcache <c>TrackedMapEntry::cleanup</c>: 直接 delegate 到內部 entry。</remarks>
    public override void Cleanup(CacheEventFlags eventFlags) => _entry.Cleanup(eventFlags);

    /// <inheritdoc />
    /// <remarks>
    /// cppcache <c>TrackedMapEntry::getVersionStamp</c> 拋
    /// <c>FatalInternalException("MapEntry::getVersionStamp for TrackedMapEntry is not applicable")</c>。
    /// Tracker 與 version-checks 是互斥的 concurrency 機制 — 一個 entry
    /// 不會同時走兩條路。
    /// </remarks>
    public override VersionStamp VersionStamp =>
        throw new InvalidOperationException(
            "VersionStamp called on TrackedMapEntry; tracker and version-checks are mutually exclusive.");
}
