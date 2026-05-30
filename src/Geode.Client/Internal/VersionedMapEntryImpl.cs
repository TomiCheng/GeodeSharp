namespace Geode.Client.Internal;

/// <summary>
/// Versioned <see cref="MapEntry"/> implementation — adds a per-entry
/// <see cref="VersionStamp"/> for concurrency-checks (race-loser /
/// concurrent-modification detection). <see cref="EntryFactory.NewEntry"/>
/// picks this variant when
/// <see cref="EntryFactory.ConcurrencyChecksEnabled"/> is
/// <see langword="true"/>. Mirrors cppcache
/// <c>VersionedMapEntryImpl</c>
/// (<c>cppcache/src/MapEntryImpl.hpp:107-125</c>).
/// </summary>
/// <remarks>
/// cppcache <c>VersionedMapEntryImpl : MapEntryImpl, VersionStamp</c>
/// 多重繼承 — 自身就是 VersionStamp,<c>getVersionStamp()</c> 回
/// <c>*this</c>。C# 無 MI,改用組合(持有 <see cref="VersionStamp"/>
/// 實例),override <c>VersionStamp</c> 屬性回該實例。
/// Skeleton only — composed stamp + override 在 concurrency-checks
/// 真接時補上。
/// </remarks>
internal sealed class VersionedMapEntryImpl : MapEntryImpl
{
    /// <summary>
    /// Composed <see cref="VersionStamp"/> (cppcache 多重繼承
    /// <c>VersionedMapEntryImpl : MapEntryImpl, VersionStamp</c> → 組合)。
    /// </summary>
    public override VersionStamp VersionStamp { get; } = new();
}
