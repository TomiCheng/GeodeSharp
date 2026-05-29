namespace Geode.Client.Internal;

/// <summary>
/// Internal exception that carries a <see cref="GfErrType"/> code —
/// used by the cppcache-mirror CRUD pipeline (mainly
/// <c>LocalUpdateAsync</c> in <see cref="LocalRegion.IRegionAction"/>)
/// to surface non-success outcomes that need code-level dispatch by
/// the pipeline (e.g. <see cref="GfErrType.CacheEntryUpdated"/>
/// race-loser, <see cref="GfErrType.CacheConcurrentModificationException"/>,
/// <see cref="GfErrType.InvalidDelta"/>).
/// </summary>
/// <remarks>
/// <para>
/// 跟 <see cref="GeodeException"/> 子類(<see cref="RegionExistsException"/>
/// / <see cref="EntryExistsException"/> / 等)的差別:那些是 user-facing
/// 的「明確 exception 型別」;這個是 pipeline-internal 訊號載體 —
/// catch 後讀 <see cref="Code"/> switch 分流處理,不該洩漏出 internal
/// 邊界。
/// </para>
/// <para>
/// 之所以走 exception 而不是 <c>Task&lt;GfErrType&gt;</c>:race-loser /
/// concurrent-mod 等情況 cppcache 走 err-code,但 .NET 港希望維持
/// <c>*Async</c> 統一 exception 設計,把 code 包進 exception 是
/// 折衷 — 性能小損失(race 罕見),語意一致性換到。
/// </para>
/// </remarks>
internal sealed class GfErrTypeException : GeodeException
{
    public GfErrType Code { get; }

    public GfErrTypeException(GfErrType code)
        : base($"Pipeline signal: {code}.")
    {
        Code = code;
    }

    public GfErrTypeException(GfErrType code, string message)
        : base(message)
    {
        Code = code;
    }

    public GfErrTypeException(GfErrType code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
